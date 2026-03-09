using Kariyer.Mail.Api.Common.Enums;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
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
            CancellationToken ct) =>
        {
            EmailJob? job = await dbContext.EmailJobs
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jobId, ct);

            if (job == null)
            {
                return Results.NotFound(new { Message = $"Job {jobId} not found." });
            }
            
            IDatabase garnet = multiplexer.GetDatabase();
            
            RedisValue totalResolved = await garnet.StringGetAsync($"job:stats:{jobId}:resolved");
            RedisValue totalSent = await garnet.StringGetAsync($"job:stats:{jobId}:sent");
            RedisValue totalFailed = await garnet.StringGetAsync($"job:stats:{jobId}:failed");

            JobMetricsDto metrics = new (
                TotalResolved: totalResolved.HasValue ? (long)totalResolved : 0,
                SuccessfullySent: totalSent.HasValue ? (long)totalSent : 0,
                FailedToDrop: totalFailed.HasValue ? (long)totalFailed : 0
            );

            JobStatusResponseDto response = new (
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