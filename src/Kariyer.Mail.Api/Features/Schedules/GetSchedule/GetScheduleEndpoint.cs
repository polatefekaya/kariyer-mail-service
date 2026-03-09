using System.Diagnostics;
using System.Text.Json;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;
using Kariyer.Mail.Api.Features.Schedules.GetSchedule.Contracts;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Schedules.GetSchedule;

internal sealed class GetScheduleEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("schedules/{id:ulid}", async (
            Ulid id,
            MailDbContext dbContext,
            IConnectionMultiplexer multiplexer,
            ILogger<GetScheduleEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("GetSingleSchedule");
            activity?.SetTag("schedule.id", id.ToString());

            string cacheKey = $"schedule:detail:{id}";
            IDatabase garnet = multiplexer.GetDatabase();

            RedisValue cachedData = await garnet.StringGetAsync(cacheKey);
            if (cachedData.HasValue)
            {
                ScheduleDetailDto? cachedSchedule = JsonSerializer.Deserialize<ScheduleDetailDto>(cachedData.ToString());
                if (cachedSchedule != null) 
                {
                    logger.LogDebug("Cache HIT for Schedule [{ScheduleId}].", id);
                    return Results.Ok(cachedSchedule);
                }
            }

            logger.LogDebug("Cache MISS for Schedule [{ScheduleId}]. Querying database.", id);

            ScheduleDetailDto? schedule = await dbContext.EmailJobSchedules
                .AsNoTracking()
                .Where(s => s.Id == id)
                .Select(s => new ScheduleDetailDto(
                    s.Id, s.Name, s.JobType.ToString(), s.TemplateId, 
                    s.Subject, s.BodyTemplate, s.Filters, 
                    s.IsRecurring, s.CronExpression, s.OneTimeExecuteAt, 
                    s.IsActive, s.CreatedAt, s.UpdatedAt))
                .FirstOrDefaultAsync(ct);

            if (schedule == null) 
            {
                logger.LogWarning("Get Schedule failed: ID [{ScheduleId}] not found.", id);
                return Results.NotFound();
            }

            string serializedData = JsonSerializer.Serialize(schedule);
            await garnet.StringSetAsync(cacheKey, serializedData, TimeSpan.FromHours(24));

            return Results.Ok(schedule);
        })
        .WithTags("Schedules");
    }
}