using System.Collections.Concurrent;
using System.Diagnostics;
using Kariyer.Mail.Api.Common.Enums;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Providers;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Features.DispatchEmail.Contracts;
using Kariyer.Mail.Api.Features.DispatchEmail.Providers;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Scriban;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.DispatchEmail;

internal sealed class DispatchEmailBatchConsumer : IConsumer<Batch<DispatchEmailCommand>>
{
    private readonly IEmailProviderFactory _providerFactory;
    private readonly MailDbContext _dbContext;
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly ILogger<DispatchEmailBatchConsumer> _logger;

    public DispatchEmailBatchConsumer(
        IEmailProviderFactory providerFactory,
        MailDbContext dbContext,
        IConnectionMultiplexer multiplexer,
        ILogger<DispatchEmailBatchConsumer> logger)
    {
        _providerFactory = providerFactory;
        _dbContext = dbContext;
        _multiplexer = multiplexer;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<Batch<DispatchEmailCommand>> context)
    {
        int batchSize = context.Message.Length;

        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("ProcessDispatchBatch");
        activity?.SetTag("batch.size", batchSize);

        IEmailProvider provider = _providerFactory.GetActiveProvider();
        string providerName = provider.GetType().Name;
        activity?.SetTag("provider.name", providerName);

        _logger.LogInformation("Processing batch of {BatchSize} targets via {ProviderName}.", batchSize, providerName);

        Ulid[] uniqueJobIds = context.Message
            .Where(m => m.Message.JobId.HasValue)
            .Select(m => m.Message.JobId!.Value)
            .Distinct()
            .ToArray();

        HashSet<Ulid> cancelledJobIds = new HashSet<Ulid>();
        IDatabase garnet = _multiplexer.GetDatabase();

        foreach (Ulid jobId in uniqueJobIds)
        {
            RedisValue isCancelled = await garnet.StringGetAsync($"job:cancelled:{jobId}");
            if (isCancelled.HasValue)
            {
                cancelledJobIds.Add(jobId);
                _logger.LogWarning("Job [{JobId}] detected as Cancelled in Garnet. Halting its targets.", jobId);
            }
        }

        DispatchEmailCommand firstCommand = context.Message.First().Message;
        Template compiledSubject = Template.Parse(firstCommand.Subject);
        Template compiledBody = Template.Parse(firstCommand.RawTemplate);

        ConcurrentBag<TargetDispatchResult> results = new();
        int successCount = 0;
        int failCount = 0;

        IEnumerable<Task> sendTasks = context.Message.Select(async messageContext =>
        {
            DispatchEmailCommand cmd = messageContext.Message;

            if (cmd.JobId.HasValue && cancelledJobIds.Contains(cmd.JobId.Value))
            {
                results.Add(new (cmd.TargetId, cmd.JobId, TargetStatus.Cancelled, "Job cancelled via Garnet kill switch."));
                return;
            }

            try
            {
                (string finalSubject, string finalBody) = await RenderContentAsync(compiledSubject, compiledBody, cmd.TemplateData);

                await provider.SendEmailAsync(cmd.Email, finalSubject, finalBody, messageContext.CancellationToken);

                results.Add(new (cmd.TargetId, cmd.JobId, TargetStatus.Sent, null));
                Interlocked.Increment(ref successCount);
            }
            catch (Exception ex)
            {
                results.Add(new (cmd.TargetId, cmd.JobId, TargetStatus.Failed, ex.Message));
                Interlocked.Increment(ref failCount);
            }
        });

        await Task.WhenAll(sendTasks);

        await FlushMetricsToGarnetAsync(results);
        
        KeyValuePair<string, object?> providerTag = new KeyValuePair<string, object?>("provider", providerName);
        if (successCount > 0) DiagnosticsConfig.EmailsSentCounter.Add(successCount, providerTag);
        if (failCount > 0) DiagnosticsConfig.EmailsFailedCounter.Add(failCount, providerTag);

        _logger.LogInformation("Batch complete. Sent: {SentCount}, Failed: {FailedCount}, Cancelled/Dropped: {DroppedCount}",
            successCount, failCount, results.Count - (successCount + failCount));

        await FlushToPostgresAsync(results.ToList(), context.CancellationToken);
    }
    
    private async Task FlushMetricsToGarnetAsync(IEnumerable<TargetDispatchResult> results)
    {
        IEnumerable<IGrouping<Ulid?, TargetDispatchResult>> groupedResults = results.GroupBy(r => r.JobId);
        IDatabase garnet = _multiplexer.GetDatabase();
    
        foreach (IGrouping<Ulid?, TargetDispatchResult> group in groupedResults)
        {
            if (!group.Key.HasValue) continue; 
            
            Ulid currentJobId = group.Key.Value;
            
            long jobSuccesses = group.Count(r => r.Status == TargetStatus.Sent);
            long jobFailures = group.Count(r => r.Status == TargetStatus.Failed);
            
            if (jobSuccesses > 0)
            {
                await garnet.StringIncrementAsync($"job:stats:{currentJobId}:sent", jobSuccesses);
            }
            
            if (jobFailures > 0)
            {
                await garnet.StringIncrementAsync($"job:stats:{currentJobId}:failed", jobFailures);
            }
        }
    }
    
    private async Task<(string Subject, string Body)> RenderContentAsync(
        Template compiledSubject, 
        Template compiledBody, 
        Dictionary<string, string> templateData)
    {
        if (templateData == null || templateData.Count == 0)
        {
            return (compiledSubject.Page.ToString(), compiledBody.Page.ToString());
        }

        string renderedSubject = await compiledSubject.RenderAsync(templateData);
        string renderedBody = await compiledBody.RenderAsync(templateData);

        return (renderedSubject, renderedBody);
    }

    private async Task FlushToPostgresAsync(List<TargetDispatchResult> results, CancellationToken ct)
    {
        if (results.Count == 0) return;

        using Activity? dbActivity = DiagnosticsConfig.MailActivitySource.StartActivity("FlushDispatchBatch");
        long startTs = Stopwatch.GetTimestamp();

        Ulid[] ids = results.Select(r => r.TargetId).ToArray();
        int[] statuses = results.Select(r => (int)r.Status).ToArray();
        string?[] errors = results.Select(r => r.Error).ToArray();

        try
        {
            await _dbContext.Database.ExecuteSqlAsync($@"
                UPDATE ""EmailTargets"" AS t
                SET 
                    ""Status"" = data.status,
                    ""ProcessedAt"" = CURRENT_TIMESTAMP,
                    ""ErrorMessage"" = data.error
                FROM (
                    SELECT UNNEST({ids}) AS id, UNNEST({statuses}) AS status, UNNEST({errors}) AS error
                ) AS data
                WHERE t.""Id"" = data.id;", 
            ct);

            dbActivity?.SetStatus(ActivityStatusCode.Ok);
            _logger.LogDebug("Flushed {RowCount} updates to PostgreSQL in {ElapsedMs}ms", 
                results.Count, Stopwatch.GetElapsedTime(startTs).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            dbActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Catastrophic failure during batched UNNEST update. {RowCount} records affected.", results.Count);
            throw; 
        }
    }
}