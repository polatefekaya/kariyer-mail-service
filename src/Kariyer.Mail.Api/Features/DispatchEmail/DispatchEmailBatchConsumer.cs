using System.Diagnostics;
using Kariyer.Mail.Api.Common.Enums;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Providers;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Features.DispatchEmail.Providers;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Scriban;
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

        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("ProcessSingleDispatch");
        activity?.SetTag("target.id", cmd.TargetId.ToString());
        if (cmd.JobId.HasValue) activity?.SetTag("job.id", cmd.JobId.Value.ToString());

        IEmailProvider provider = _providerFactory.GetActiveProvider();
        string providerName = provider.GetType().Name;
        activity?.SetTag("provider.name", providerName);

        if (cmd.JobId.HasValue)
        {
            IDatabase garnet = _multiplexer.GetDatabase();
            RedisValue isCancelled = await garnet.StringGetAsync($"job:cancelled:{cmd.JobId.Value}");
            
            if (isCancelled.HasValue)
            {
                _logger.LogWarning("Job [{JobId}] detected as Cancelled in Garnet. Halting target [{TargetId}].", cmd.JobId.Value, cmd.TargetId);
                await UpdateTargetStatusAsync(cmd.TargetId, TargetStatus.Cancelled, "Job cancelled via Garnet kill switch.", context.CancellationToken);
                return;
            }
        }

        Template compiledSubject = Template.Parse(cmd.Subject);
        Template compiledBody = Template.Parse(cmd.RawTemplate);
        
        string finalSubject = cmd.Subject;
        string finalBody = cmd.RawTemplate;

        if (cmd.TemplateData != null && cmd.TemplateData.Count > 0)
        {
            finalSubject = await compiledSubject.RenderAsync(cmd.TemplateData);
            finalBody = await compiledBody.RenderAsync(cmd.TemplateData);
        }

        TargetStatus finalStatus;
        string? errorMessage = null;

        try
        {
            await provider.SendEmailAsync(cmd.Email, finalSubject, finalBody, context.CancellationToken);
            
            finalStatus = TargetStatus.Sent;
            
            DiagnosticsConfig.EmailsSentCounter.Add(1, new KeyValuePair<string, object?>("provider", providerName));
            _logger.LogDebug("Successfully dispatched email to {Email} for Target [{TargetId}]", cmd.Email, cmd.TargetId);
        }
        catch (Exception ex)
        {
            finalStatus = TargetStatus.Failed;
            errorMessage = ex.Message;
            
            DiagnosticsConfig.EmailsFailedCounter.Add(1, new KeyValuePair<string, object?>("provider", providerName));
            _logger.LogError(ex, "Failed to dispatch email to {Email} for Target [{TargetId}]", cmd.Email, cmd.TargetId);
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
            _logger.LogDebug("Updated Target [{TargetId}] to {Status} in {ElapsedMs}ms", 
                targetId, status.ToString(), Stopwatch.GetElapsedTime(startTs).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            dbActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Catastrophic failure updating database for Target [{TargetId}].", targetId);
            throw; 
        }
    }
}