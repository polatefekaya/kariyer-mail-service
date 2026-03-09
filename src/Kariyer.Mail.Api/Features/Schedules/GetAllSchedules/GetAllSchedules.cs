using System.Diagnostics;
using System.Text.Json;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;
using Kariyer.Mail.Api.Features.Schedules.GetAllSchedules.Contracts;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Schedules.GetAllSchedules;

internal sealed class GetAllSchedulesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("schedules", async (
            bool? includeInactive,
            MailDbContext dbContext,
            IConnectionMultiplexer multiplexer,
            ILogger<GetAllSchedulesEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("GetAllSchedules");
            
            string cacheKey = $"schedules:all:inactive_{includeInactive ?? false}";
            activity?.SetTag("cache.key", cacheKey);
            
            IDatabase garnet = multiplexer.GetDatabase();

            RedisValue cachedData = await garnet.StringGetAsync(cacheKey);
            if (cachedData.HasValue)
            {
                List<ScheduleSummaryDto>? cachedSchedules = JsonSerializer.Deserialize<List<ScheduleSummaryDto>>(cachedData.ToString());
                if (cachedSchedules != null) 
                {
                    logger.LogDebug("Cache HIT for {CacheKey}. Returning {Count} schedules.", cacheKey, cachedSchedules.Count);
                    activity?.SetTag("cache.hit", true);
                    return Results.Ok(cachedSchedules);
                }
            }

            logger.LogDebug("Cache MISS for {CacheKey}. Querying PostgreSQL.", cacheKey);
            activity?.SetTag("cache.hit", false);

            var query = dbContext.EmailJobSchedules.AsNoTracking();

            if (includeInactive != true)
            {
                query = query.Where(s => s.IsActive);
            }

            List<ScheduleSummaryDto> schedules = await query
                .OrderByDescending(e => e.CreatedAt) 
                .Select(e => new ScheduleSummaryDto( 
                    e.Id, 
                    e.Name, 
                    e.JobType.ToString(), 
                    e.IsRecurring, 
                    e.CronExpression, 
                    e.OneTimeExecuteAt, 
                    e.IsActive, 
                    e.CreatedAt
                ))
                .ToListAsync(ct);

            string serializedData = JsonSerializer.Serialize(schedules);
            await garnet.StringSetAsync(cacheKey, serializedData, TimeSpan.FromHours(1));

            logger.LogInformation("Fetched {Count} schedules from database and updated Garnet cache.", schedules.Count);
            return Results.Ok(schedules);
        })
        .WithTags("Schedules");
    }
}