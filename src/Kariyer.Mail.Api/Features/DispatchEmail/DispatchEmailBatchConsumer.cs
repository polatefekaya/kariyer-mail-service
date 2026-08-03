using System.Diagnostics;
using Kariyer.Mail.Api.Common.Enums;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Providers;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Templating;
using Kariyer.Mail.Api.Features.DispatchEmail.Providers;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
using Scriban.Syntax;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.DispatchEmail;

internal sealed class DispatchEmailConsumer : IConsumer<DispatchEmailCommand>
{
    private readonly IEmailProviderFactory _providerFactory;
    private readonly MailDbContext _dbContext;
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly ILogger<DispatchEmailConsumer> _logger;

    public DispatchEmailConsumer(
        IEmailProviderFactory providerFactory,
        MailDbContext dbContext,
        IConnectionMultiplexer multiplexer,
        ILogger<DispatchEmailConsumer> logger)
    {
        _providerFactory = providerFactory;
        _dbContext = dbContext;
        _multiplexer = multiplexer;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<DispatchEmailCommand> context)
    {
        DispatchEmailCommand cmd = context.Message;

        IEmailProvider provider = _providerFactory.GetActiveProvider();
        string providerName = provider.GetType().Name.Replace("EmailProvider", "").ToLowerInvariant();

        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity(
            "mail.dispatch", ActivityKind.Consumer);
        activity?.SetTag("mail.target_id", cmd.TargetId.ToString());
        activity?.SetTag("mail.has_job", cmd.JobId.HasValue);
        activity?.SetTag("mail.provider", providerName);
        activity?.SetTag("mail.recipient_type", cmd.JobId.HasValue ? "bulk" : "transactional");
        if (cmd.JobId.HasValue) activity?.SetTag("mail.job_id", cmd.JobId.Value.ToString());

        // Check kill switch before doing any work
        if (cmd.JobId.HasValue)
        {
            IDatabase garnet = _multiplexer.GetDatabase();
            RedisValue isCancelled = await garnet.StringGetAsync($"job:cancelled:{cmd.JobId.Value}");

            if (isCancelled.HasValue)
            {
                activity?.SetTag("mail.status", "cancelled");
                _logger.LogWarning(
                    "Job [{JobId}] cancelled via kill switch. Halting target [{TargetId}].",
                    cmd.JobId.Value, cmd.TargetId);
                await UpdateTargetStatusAsync(cmd.TargetId, TargetStatus.Cancelled,
                    "Job cancelled via Garnet kill switch.", context.CancellationToken);
                return;
            }
        }

        string finalSubject;
        string finalBody;

        try
        {
            (finalSubject, finalBody) = await RenderAsync(cmd);
        }
        catch (Exception ex)
        {
            // A template that fails to parse or render fails identically on every retry, so
            // rethrowing would burn the retry budget and dead-letter the message while leaving the
            // target row stuck on Pending forever. Record it as a permanent failure instead.
            activity?.SetTag("mail.status", "render_failed");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);

            DiagnosticsConfig.EmailsFailedCounter.Add(1,
                new KeyValuePair<string, object?>("provider", providerName),
                new KeyValuePair<string, object?>("exception_type", ex.GetType().Name));

            _logger.LogError(ex,
                "Template render failed for Target [{TargetId}] (Job: {JobId}). Marking as failed without retry.",
                cmd.TargetId, cmd.JobId?.ToString() ?? "none");

            if (cmd.JobId.HasValue)
            {
                IDatabase statsDb = _multiplexer.GetDatabase();
                await statsDb.StringIncrementAsync($"job:stats:{cmd.JobId.Value}:failed");
            }

            await UpdateTargetStatusAsync(cmd.TargetId, TargetStatus.Failed,
                $"Template render failed: {ex.Message}", context.CancellationToken);
            return;
        }

        TargetStatus finalStatus;
        string? errorMessage = null;

        try
        {
            await provider.SendEmailAsync(cmd.Email, finalSubject, finalBody, context.CancellationToken);

            finalStatus = TargetStatus.Sent;
            activity?.SetTag("mail.status", "sent");
            activity?.SetStatus(ActivityStatusCode.Ok);

            DiagnosticsConfig.EmailsSentCounter.Add(1,
                new KeyValuePair<string, object?>("provider", providerName),
                new KeyValuePair<string, object?>("recipient_type", cmd.JobId.HasValue ? "bulk" : "transactional"));

            _logger.LogInformation(
                "Dispatched email via {Provider} for Target [{TargetId}] (Job: {JobId})",
                providerName, cmd.TargetId, cmd.JobId?.ToString() ?? "none");
        }
        catch (Exception ex)
        {
            finalStatus = TargetStatus.Failed;
            errorMessage = ex.Message;

            activity?.SetTag("mail.status", "failed");
            activity?.SetTag("mail.error", ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);

            DiagnosticsConfig.EmailsFailedCounter.Add(1,
                new KeyValuePair<string, object?>("provider", providerName),
                new KeyValuePair<string, object?>("exception_type", ex.GetType().Name));

            _logger.LogError(ex,
                "Failed to dispatch email via {Provider} for Target [{TargetId}] (Job: {JobId})",
                providerName, cmd.TargetId, cmd.JobId?.ToString() ?? "none");
        }

        if (cmd.JobId.HasValue)
        {
            IDatabase garnet = _multiplexer.GetDatabase();
            string metricSuffix = finalStatus == TargetStatus.Sent ? "sent" : "failed";
            await garnet.StringIncrementAsync($"job:stats:{cmd.JobId.Value}:{metricSuffix}");
        }

        await UpdateTargetStatusAsync(cmd.TargetId, finalStatus, errorMessage, context.CancellationToken);
    }

    /// <summary>
    /// Renders subject and body against the command's template data.
    ///
    /// Two things this deliberately does that the previous version did not. It normalises the
    /// stored content first, because templates saved before the editor was fixed still carry
    /// HTML-encoded Scriban delimiters. And it renders unconditionally: skipping the render when
    /// there was no template data is what mailed people a literal <c>{{ FullName }}</c>.
    /// </summary>
    private async Task<(string Subject, string Body)> RenderAsync(DispatchEmailCommand cmd)
    {
        string subjectSource = ScribanContentNormalizer.Normalize(cmd.Subject);
        string bodySource = ScribanContentNormalizer.Normalize(cmd.RawTemplate);

        Template compiledSubject = Template.Parse(subjectSource);
        Template compiledBody = Template.Parse(bodySource);

        if (compiledSubject.HasErrors || compiledBody.HasErrors)
        {
            string messages = string.Join("; ",
                compiledSubject.Messages.Concat(compiledBody.Messages).Select(m => m.ToString()));
            throw new InvalidOperationException($"Scriban syntax error: {messages}");
        }

        ScriptObject scriptObject = new();
        scriptObject.Import(cmd.TemplateData ?? new Dictionary<string, string>());

        List<string> unresolved = [];

        TemplateContext templateContext = new()
        {
            MemberRenamer = member => member.Name,
            StrictVariables = false
        };
        templateContext.TryGetVariable = (TemplateContext _, SourceSpan _, ScriptVariable variable, out object? value) =>
        {
            unresolved.Add(variable.Name);
            value = string.Empty;
            return true;
        };
        templateContext.PushGlobal(scriptObject);

        string subject = await compiledSubject.RenderAsync(templateContext);
        string body = await compiledBody.RenderAsync(templateContext);

        if (unresolved.Count > 0)
        {
            // Not fatal — this matches the old StrictVariables=false behaviour — but it used to be
            // completely invisible, which is how templates authored against the wrong vocabulary
            // went out blank without anyone noticing.
            _logger.LogWarning(
                "Target [{TargetId}] rendered with {Count} unresolved variable(s): {Variables}. They were emitted as empty strings.",
                cmd.TargetId, unresolved.Count, string.Join(", ", unresolved.Distinct()));
        }

        return (subject, body);
    }

    private async Task UpdateTargetStatusAsync(Ulid targetId, TargetStatus status, string? error, CancellationToken ct)
    {
        using Activity? dbActivity = DiagnosticsConfig.MailActivitySource.StartActivity("UpdateTargetStatus");
        long startTs = Stopwatch.GetTimestamp();

        try
        {
            await _dbContext.EmailTargets
                .Where(t => t.Id == targetId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, status)
                    .SetProperty(t => t.ProcessedAt, DateTime.UtcNow)
                    .SetProperty(t => t.ErrorMessage, error), ct);

            dbActivity?.SetStatus(ActivityStatusCode.Ok);
            _logger.LogDebug("Updated Target [{TargetId}] → {Status} in {ElapsedMs}ms",
                targetId, status, Stopwatch.GetElapsedTime(startTs).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            dbActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            dbActivity?.AddException(ex);
            _logger.LogError(ex, "DB failure updating Target [{TargetId}] to {Status}.", targetId, status);
            throw;
        }
    }
}
