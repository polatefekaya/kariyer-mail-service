using System.Text.Json;
using Kariyer.Mail.Api.Common.Persistence;
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
            CancellationToken ct) =>
        {
            string cacheKey = $"schedules:all:inactive_{includeInactive ?? false}";
            IDatabase garnet = multiplexer.GetDatabase();

            RedisValue cachedData = await garnet.StringGetAsync(cacheKey);
            if (cachedData.HasValue)
            {
                List<ScheduleSummaryDto>? cachedSchedules = JsonSerializer.Deserialize<List<ScheduleSummaryDto>>(cachedData.ToString());
                if (cachedSchedules != null) return Results.Ok(cachedSchedules);
            }

            var query = dbContext.EmailJobSchedules.AsNoTracking();

            if (includeInactive != true)
            {
                query = query.Where(s => s.IsActive);
            }

            List<ScheduleSummaryDto> schedules = await query
                .Select(s => new ScheduleSummaryDto(
                    s.Id, s.Name, s.JobType.ToString(), s.IsRecurring, 
                    s.CronExpression, s.OneTimeExecuteAt, s.IsActive, s.CreatedAt))
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync(ct);

            string serializedData = JsonSerializer.Serialize(schedules);
            await garnet.StringSetAsync(cacheKey, serializedData, TimeSpan.FromHours(1));

            return Results.Ok(schedules);
        })
        .WithTags("Schedules");
    }
}