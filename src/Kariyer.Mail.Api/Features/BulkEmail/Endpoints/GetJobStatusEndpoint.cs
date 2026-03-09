using System.Diagnostics;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;
using Kariyer.Mail.Api.Features.BulkEmail.Contracts;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.BulkEmail.Endpoints;

internal sealed class GetJobStatusEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("jobs/bulk/{jobId:ulid}", async (
            Ulid jobId,
            MailDbContext dbContext,
            IConnectionMultiplexer multiplexer,
            ILogger<GetJobStatusEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("GetSingleJobStatus");
            activity?.SetTag("job.id", jobId.ToString());

            EmailJob? job = await dbContext.EmailJobs
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jobId, ct);

            if (job == null)
            {
                logger.LogWarning("Status check failed: Job [{JobId}] not found.", jobId);
                return Results.NotFound(new { Message = $"Job {jobId} not found." });
            }
            
            IDatabase garnet = multiplexer.GetDatabase();
            
            RedisValue totalResolved = await garnet.StringGetAsync($"job:stats:{jobId}:resolved");
            RedisValue totalSent = await garnet.StringGetAsync($"job:stats:{jobId}:sent");
            RedisValue totalFailed = await garnet.StringGetAsync($"job:stats:{jobId}:failed");

            JobMetricsDto metrics = new(
                TotalResolved: totalResolved.HasValue ? (long)totalResolved : 0,
                SuccessfullySent: totalSent.HasValue ? (long)totalSent : 0,
                FailedToDrop: totalFailed.HasValue ? (long)totalFailed : 0
            );

            logger.LogDebug("Fetched metrics for Job [{JobId}]. Resolved: {Resolved}, Sent: {Sent}, Failed: {Failed}", 
                jobId, metrics.TotalResolved, metrics.SuccessfullySent, metrics.FailedToDrop);

            JobStatusResponseDto response = new(
                Id: job.Id,
                Status: job.Status.ToString(),
                Type: job.JobType.ToString(),
                StartedAt: job.CreatedAt,
                CompletedAt: job.CompletedAt,
                ErrorMessage: job.ErrorMessage,
                Metrics: metrics
            );

            return Results.Ok(response);
        })
        .WithTags("Bulk Email");
    }
}