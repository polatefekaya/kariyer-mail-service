using System.Diagnostics;
using Hangfire;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Schedules.DeleteSchedule;

internal sealed class DeleteScheduleEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("schedules/{id:ulid}", async (
            Ulid id,
            MailDbContext dbContext,
            IRecurringJobManager recurringJobs,
            IConnectionMultiplexer multiplexer,
            ILogger<DeleteScheduleEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("DeleteSchedule");
            activity?.SetTag("schedule.id", id.ToString());

            int updatedRows = await dbContext.EmailJobSchedules
                .Where(s => s.Id == id && s.IsActive)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), ct);
                
            if (updatedRows == 0) 
            {
                logger.LogWarning("Deletion failed: Schedule [{ScheduleId}] not found or already inactive.", id);
                return Results.NotFound();
            }

            recurringJobs.RemoveIfExists(id.ToString());
            logger.LogDebug("Removed Schedule [{ScheduleId}] from Hangfire recurring jobs manager.", id);
            
            IDatabase garnet = multiplexer.GetDatabase();
            await garnet.KeyDeleteAsync("schedules:all:inactive_false");
            await garnet.KeyDeleteAsync("schedules:all:inactive_true");
            await garnet.KeyDeleteAsync($"schedule:detail:{id}");
            
            logger.LogInformation("Schedule [{ScheduleId}] successfully soft-deleted and caches cleared.", id);
            return Results.NoContent();
        })
        .WithTags("Schedules");
    }
}