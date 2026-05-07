using System.Diagnostics;
using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Common.Enums;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kariyer.Mail.Api.Common.Jobs;

public sealed class TargetRetentionJob
{
    private static readonly TargetStatus[] TerminalStatuses =
    [
        TargetStatus.Sent,
        TargetStatus.Failed,
        TargetStatus.Bounced,
        TargetStatus.Cancelled
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<RetentionSettings> _settings;
    private readonly ILogger<TargetRetentionJob> _logger;

    public TargetRetentionJob(
        IServiceScopeFactory scopeFactory,
        IOptions<RetentionSettings> settings,
        ILogger<TargetRetentionJob> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        RetentionSettings config = _settings.Value;
        DateTime cutoff = DateTime.UtcNow.AddDays(-config.TargetRetentionDays);

        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("TargetRetentionJob");
        activity?.SetTag("retention.cutoff_date", cutoff.ToString("O"));
        activity?.SetTag("retention.batch_size", config.DeletionBatchSize);

        _logger.LogInformation(
            "Target retention job started. Deleting processed targets older than {CutoffDate} (retention: {Days}d).",
            cutoff, config.TargetRetentionDays);

        long totalDeleted = 0;
        int batchNumber = 0;
        long startTs = Stopwatch.GetTimestamp();

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                using IServiceScope scope = _scopeFactory.CreateScope();
                MailDbContext dbContext = scope.ServiceProvider.GetRequiredService<MailDbContext>();

                int deleted = await dbContext.EmailTargets
                    .Where(t => TerminalStatuses.Contains(t.Status) && t.ProcessedAt < cutoff)
                    .Take(config.DeletionBatchSize)
                    .ExecuteDeleteAsync(ct);

                if (deleted == 0)
                    break;

                totalDeleted += deleted;
                batchNumber++;

                DiagnosticsConfig.TargetsDeletedCounter.Add(deleted);

                _logger.LogDebug(
                    "Retention batch {BatchNumber}: deleted {BatchDeleted} targets. Running total: {Total}.",
                    batchNumber, deleted, totalDeleted);
            }

            TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
            activity?.SetTag("retention.total_deleted", totalDeleted);
            activity?.SetTag("retention.batches", batchNumber);
            activity?.SetStatus(ActivityStatusCode.Ok);

            _logger.LogInformation(
                "Target retention job completed. Deleted {TotalDeleted} targets across {Batches} batch(es) in {ElapsedMs}ms.",
                totalDeleted, batchNumber, elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);

            _logger.LogError(ex,
                "Target retention job failed after deleting {TotalDeleted} targets across {Batches} batch(es).",
                totalDeleted, batchNumber);

            throw;
        }
    }
}
