using System.Diagnostics;
using Kariyer.Mail.Api.Common.Enums;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Features.BulkEmail.Services;
using Kariyer.Mail.Api.Features.DispatchEmail;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.BulkEmail;

internal sealed class ResolverConsumer : IConsumer<StartBulkEmailJobCommand>
{
    private readonly MailDbContext _dbContext;
    private readonly ITargetResolutionService _resolutionService;
    private readonly ILogger<ResolverConsumer> _logger;
    private readonly IConnectionMultiplexer _multiplexer;

    public ResolverConsumer(
        MailDbContext dbContext, 
        ITargetResolutionService resolutionService,
        IConnectionMultiplexer multiplexer,
        ILogger<ResolverConsumer> logger)
    {
        _dbContext = dbContext;
        _resolutionService = resolutionService;
        _multiplexer = multiplexer;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<StartBulkEmailJobCommand> context)
    {
        StartBulkEmailJobCommand command = context.Message;
        
        _logger.LogInformation("Received resolution request for Bulk Email Job [{JobId}]", command.JobId);

        EmailJob? job = await _dbContext.EmailJobs.FirstOrDefaultAsync(j => j.Id == command.JobId, context.CancellationToken);
        if (job == null || job.Status != EmailJobStatus.Pending)
        {
            _logger.LogWarning("Skipping Job [{JobId}]. Status is {Status}", command.JobId, job?.Status.ToString() ?? "NULL");
            return;
        }

        using Activity? jobActivity = DiagnosticsConfig.MailActivitySource.StartActivity("ExecuteBulkResolutionJob");
        jobActivity?.SetTag("job.id", job.Id);

        try
        {
            job.MarkAsResolving();
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            string finalSubjectTemplate = job.Subject ?? string.Empty;
            string finalBodyTemplate = job.BodyTemplate ?? string.Empty;

            if (job.TemplateId.HasValue)
            {
                EmailTemplate? template = await _dbContext.EmailTemplates
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == job.TemplateId.Value, context.CancellationToken);

                if (template != null)
                {
                    finalSubjectTemplate = template.SubjectTemplate;
                    finalBodyTemplate = template.HtmlContent;
                }
                else
                {
                    throw new InvalidOperationException($"Job requested Template [{job.TemplateId.Value}] but it was not found in the database.");
                }
            }

            _logger.LogInformation("Job [{JobId}] marked as Resolving. Starting loop...", job.Id);

            IDatabase garnet = _multiplexer.GetDatabase();

            int pageNumber = 1;
            const int batchSize = 1000;
            int totalProcessed = 0;
            bool hasMoreUsers = true;

            while (hasMoreUsers)
            {
                RedisValue isCancelled = await garnet.StringGetAsync($"job:cancelled:{job.Id}");
                if (isCancelled.HasValue)
                {
                    _logger.LogWarning("Job [{JobId}] was cancelled via Garnet kill switch at page {PageNumber}. Halting worker.", job.Id, pageNumber);
                    break;
                }
                
                using Activity? batchActivity = DiagnosticsConfig.MailActivitySource.StartActivity("ProcessResolutionBatch");
                batchActivity?.SetTag("batch.page", pageNumber);

                long startTimestamp = Stopwatch.GetTimestamp();

                _logger.LogDebug("Fetching page {PageNumber} for Job [{JobId}]", pageNumber, job.Id);

                List<ResolvedTarget> users = await _resolutionService.ResolveTargetsAsync(job.Payload, pageNumber, batchSize, context.CancellationToken);

                if (users.Count == 0)
                {
                    hasMoreUsers = false;
                    break;
                }

                List<EmailTarget> targets = new List<EmailTarget>(users.Count);
                foreach (ResolvedTarget user in users)
                {
                    targets.Add(new EmailTarget(job.Id, user.TargetId, user.Email, finalSubjectTemplate, finalBodyTemplate));
                }

                await _dbContext.EmailTargets.AddRangeAsync(targets, context.CancellationToken);
                
                List<DispatchEmailCommand> dispatchCommands = new List<DispatchEmailCommand>(targets.Count);
                foreach (EmailTarget target in targets)
                {
                    Dictionary<string, string> templateData = new Dictionary<string, string>
                    {
                        { "Email", target.RecipientEmail }
                        // legacy API can return names, or any other thing, so we will add them here: { "Name", target.Name }
                    };

                    dispatchCommands.Add(new DispatchEmailCommand(
                        command.JobId, 
                        target.Id, 
                        target.RecipientEmail, 
                        finalSubjectTemplate, 
                        finalBodyTemplate, 
                        templateData));
                }

                await context.PublishBatch(dispatchCommands, context.CancellationToken);

                await _dbContext.SaveChangesAsync(context.CancellationToken);
                _dbContext.ChangeTracker.Clear();

                await garnet.StringIncrementAsync($"job:stats:{job.Id}:resolved", targets.Count);

                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTimestamp);
                KeyValuePair<string, object?> metricTag = new KeyValuePair<string, object?>("job_type", job.JobType.ToString());
                
                DiagnosticsConfig.ResolutionBatchDuration.Record(elapsed.TotalMilliseconds, metricTag);
                DiagnosticsConfig.TargetsResolvedCounter.Add(targets.Count, metricTag);

                totalProcessed += targets.Count;
                _logger.LogInformation("Job [{JobId}] - Processed page {PageNumber} ({TargetCount} targets) in {ElapsedMs}ms", 
                    job.Id, pageNumber, targets.Count, elapsed.TotalMilliseconds);

                pageNumber++;
            }

            job.MarkAsCompleted();
            _dbContext.EmailJobs.Update(job);
            await _dbContext.SaveChangesAsync(context.CancellationToken);
            
            jobActivity?.SetStatus(ActivityStatusCode.Ok);
            _logger.LogInformation("Job [{JobId}] completed. Total targets: {TotalTargets}", job.Id, totalProcessed);
        }
        catch (Exception ex)
        {
            jobActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            jobActivity?.AddException(ex);

            _logger.LogError(ex, "Catastrophic failure in Job [{JobId}]: {ErrorMessage}", job.Id, ex.Message);

            job.MarkAsFailed(ex.Message);
            _dbContext.EmailJobs.Update(job);
            await _dbContext.SaveChangesAsync(context.CancellationToken);
            throw; 
        }
    }
}