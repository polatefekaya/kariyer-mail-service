using System.Diagnostics;
using Kariyer.Mail.Api.Common.Enums;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Providers;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Features.DispatchEmail.Providers;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Scriban;
using Scriban.Runtime;
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

        Template compiledSubject = Template.Parse(cmd.Subject);
        Template compiledBody = Template.Parse(cmd.RawTemplate);

        string finalSubject = cmd.Subject;
        string finalBody = cmd.RawTemplate;

        if (cmd.TemplateData is { Count: > 0 })
        {
            ScriptObject scriptObject = new();
            scriptObject.Import(cmd.TemplateData);

            TemplateContext templateContext = new()
            {
                MemberRenamer = member => member.Name,
                StrictVariables = false
            };
            templateContext.PushGlobal(scriptObject);

            finalSubject = await compiledSubject.RenderAsync(templateContext);
            finalBody = await compiledBody.RenderAsync(templateContext);
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
