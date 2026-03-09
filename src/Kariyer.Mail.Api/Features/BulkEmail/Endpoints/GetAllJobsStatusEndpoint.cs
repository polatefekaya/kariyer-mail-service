using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Web;
using Kariyer.Mail.Api.Features.BulkEmail.Contracts;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.BulkEmail.Endpoints;

internal sealed class GetAllJobStatusEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("jobs/bulk", async (
            int? limit,
            MailDbContext dbContext,
            IConnectionMultiplexer multiplexer,
            CancellationToken ct) =>
        {
            int take = limit ?? 50;
            List<EmailJob> jobs = await dbContext.EmailJobs
                .AsNoTracking()
                .OrderByDescending(j => j.CreatedAt)
                .Take(take)
                .ToListAsync(ct);

            if (jobs.Count == 0)
            {
                return Results.Ok(new List<JobStatusResponseDto>());
            }

            IDatabase garnet = multiplexer.GetDatabase();

            RedisKey[] redisKeys = new RedisKey[jobs.Count * 3];
            
            for (int i = 0; i < jobs.Count; i++)
            {
                Ulid jobId = jobs[i].Id;
                redisKeys[i * 3] = $"job:stats:{jobId}:resolved";
                redisKeys[(i * 3) + 1] = $"job:stats:{jobId}:sent";
                redisKeys[(i * 3) + 2] = $"job:stats:{jobId}:failed";
            }

            RedisValue[] redisValues = await garnet.StringGetAsync(redisKeys);

            List<JobStatusResponseDto> response = new List<JobStatusResponseDto>(jobs.Count);
            
            for (int i = 0; i < jobs.Count; i++)
            {
                EmailJob job = jobs[i];
                
                RedisValue resolvedVal = redisValues[i * 3];
                RedisValue sentVal = redisValues[(i * 3) + 1];
                RedisValue failedVal = redisValues[(i * 3) + 2];

                JobMetricsDto metrics = new (
                    TotalResolved: resolvedVal.HasValue ? (long)resolvedVal : 0,
                    SuccessfullySent: sentVal.HasValue ? (long)sentVal : 0,
                    FailedToDrop: failedVal.HasValue ? (long)failedVal : 0
                );

                response.Add(new JobStatusResponseDto(
                    Id: job.Id,
                    Status: job.Status.ToString(),
                    Type: job.JobType.ToString(),
                    StartedAt: job.CreatedAt,
                    CompletedAt: job.CompletedAt,
                    ErrorMessage: job.ErrorMessage,
                    Metrics: metrics
                ));
            }

            return Results.Ok(response);
        })
        .WithTags("Bulk Email");
    }
}