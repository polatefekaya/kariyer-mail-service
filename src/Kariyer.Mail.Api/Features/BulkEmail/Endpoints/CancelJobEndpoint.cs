using Kariyer.Mail.Api.Common.Enums;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
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
            CancellationToken ct) =>
        {
            EmailJob? job = await dbContext.EmailJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);

            if (job == null) return Results.NotFound();
            
            if (job.Status == EmailJobStatus.Completed || job.Status == EmailJobStatus.Cancelled)
            {
                return Results.BadRequest(new { Message = "Cannot cancel this job. It is already completed or cancelled." });
            }

            IDatabase garnet = multiplexer.GetDatabase();
            await garnet.StringSetAsync($"job:cancelled:{jobId}", "1", TimeSpan.FromDays(7));

            job.MarkAsCancelled("Cancelled by Administrator.");
            
            int cancelledCount = await dbContext.EmailTargets
                .Where(t => t.JobId == jobId && (t.Status == TargetStatus.Pending || t.Status == TargetStatus.Queued))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, TargetStatus.Cancelled)
                    .SetProperty(t => t.ProcessedAt, DateTime.UtcNow)
                    .SetProperty(t => t.ErrorMessage, "Cancelled by admin before dispatch."), 
                ct);

            await dbContext.SaveChangesAsync(ct);

            return Results.Ok(new { Message = "Job cancelled.", TargetsHalted = cancelledCount });
        })
        .WithTags("Bulk Email");
    }
}