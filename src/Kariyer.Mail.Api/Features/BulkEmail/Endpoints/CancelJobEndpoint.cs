using System.Diagnostics;
using Kariyer.Mail.Api.Common.Enums;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.BulkEmail.Endpoints;

internal sealed class CancelJobEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("jobs/bulk/{jobId:ulid}/cancel", async (
            Ulid jobId,
            MailDbContext dbContext,
            IConnectionMultiplexer multiplexer,
            ILogger<CancelJobEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("CancelBulkJob");
            activity?.SetTag("job.id", jobId.ToString());

            logger.LogInformation("Received cancellation request for Job [{JobId}].", jobId);

            EmailJob? job = await dbContext.EmailJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);

            if (job == null) 
            {
                logger.LogWarning("Cancellation failed: Job [{JobId}] not found in database.", jobId);
                return Results.NotFound();
            }
            
            if (job.Status == EmailJobStatus.Completed || job.Status == EmailJobStatus.Cancelled)
            {
                logger.LogWarning("Cancellation rejected: Job [{JobId}] is already {JobStatus}.", jobId, job.Status);
                return Results.BadRequest(new { Message = $"Cannot cancel this job. It is already {job.Status}." });
            }

            try
            {
                // 1. Engage Garnet Kill Switch
                IDatabase garnet = multiplexer.GetDatabase();
                await garnet.StringSetAsync($"job:cancelled:{jobId}", "1", TimeSpan.FromDays(7));
                logger.LogDebug("Garnet kill-switch engaged for Job [{JobId}].", jobId);

                // 2. Mark Job Entity
                job.MarkAsCancelled("Cancelled by Administrator.");
                
                // 3. Halt Pending Targets
                int cancelledCount = await dbContext.EmailTargets
                    .Where(t => t.JobId == jobId && (t.Status == TargetStatus.Pending || t.Status == TargetStatus.Queued))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.Status, TargetStatus.Cancelled)
                        .SetProperty(t => t.ProcessedAt, DateTime.UtcNow)
                        .SetProperty(t => t.ErrorMessage, "Cancelled by admin before dispatch."), 
                    ct);

                await dbContext.SaveChangesAsync(ct);

                logger.LogInformation("Job [{JobId}] successfully cancelled. {CancelledCount} pending targets were halted.", jobId, cancelledCount);
                activity?.SetTag("targets.halted", cancelledCount);

                return Results.Ok(new { Message = "Job cancelled.", TargetsHalted = cancelledCount });
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                logger.LogError(ex, "Database failure while attempting to cancel Job [{JobId}].", jobId);
                throw;
            }
        })
        .WithTags("Bulk Email");
    }
}