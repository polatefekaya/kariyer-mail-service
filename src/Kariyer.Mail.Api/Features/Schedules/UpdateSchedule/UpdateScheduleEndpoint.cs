using System.Diagnostics;
using Hangfire;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;
using Kariyer.Mail.Api.Features.Schedules.Execution;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Schedules.UpdateSchedule;

internal sealed class UpdateScheduleEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("schedules/{id:ulid}", async (
            Ulid id,
            UpdateScheduleRequest request,
            MailDbContext dbContext,
            IBackgroundJobClient backgroundJobs,
            IRecurringJobManager recurringJobs,
            IConnectionMultiplexer multiplexer,
            ILogger<UpdateScheduleEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("UpdateSchedule");
            activity?.SetTag("schedule.id", id.ToString());

            if (!request.IsRecurring && request.OneTimeExecuteAt.HasValue)
            {
                if (request.OneTimeExecuteAt.Value < DateTimeOffset.UtcNow.AddMinutes(1))
                {
                    logger.LogWarning("Rejecting schedule update. Execution time {ExecutionTime} is in the past.", request.OneTimeExecuteAt.Value);
                    return Results.BadRequest(new { Message = "Execution time must be safely in the future." });
                }
            }

            EmailJobSchedule? schedule = await dbContext.EmailJobSchedules
                .FirstOrDefaultAsync(s => s.Id == id && s.IsActive, ct);

            if (schedule == null)
            {
                logger.LogWarning("Update failed: Schedule [{ScheduleId}] not found or inactive.", id);
                return Results.NotFound();
            }

            schedule.Update(
                request.Name, request.TemplateId, request.Subject,
                request.BodyTemplate, request.Filters, request.IsRecurring,
                request.CronExpression, request.OneTimeExecuteAt);

            await dbContext.SaveChangesAsync(ct);

            IDatabase garnet = multiplexer.GetDatabase();
            await garnet.KeyDeleteAsync("schedules:all:inactive_false");
            await garnet.KeyDeleteAsync("schedules:all:inactive_true");
            await garnet.KeyDeleteAsync($"schedule:detail:{id}");

            string hangfireJobId = schedule.Id.ToString();

            if (schedule.IsRecurring && !string.IsNullOrWhiteSpace(schedule.CronExpression))
            {
                recurringJobs.AddOrUpdate<ScheduleTriggerInvoker>(
                    recurringJobId: hangfireJobId,
                    methodCall: invoker => invoker.ExecuteScheduleAsync(schedule.Id.ToString()),
                    cronExpression: schedule.CronExpression,
                    new RecurringJobOptions
                    {
                        TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")
                    }
                );

                logger.LogInformation("Updated Schedule [{ScheduleId}] - Re-registered as recurring with CRON: {CronExpression}", id, schedule.CronExpression);
            }
            else
            {
                recurringJobs.RemoveIfExists(hangfireJobId);

                if (schedule.OneTimeExecuteAt.HasValue)
                {
                    backgroundJobs.Schedule<ScheduleTriggerInvoker>(
                        methodCall: invoker => invoker.ExecuteScheduleAsync(schedule.Id.ToString()),
                        enqueueAt: schedule.OneTimeExecuteAt.Value);

                    logger.LogInformation("Updated Schedule [{ScheduleId}] - Converted to one-time execution at: {ExecuteAt}", id, schedule.OneTimeExecuteAt.Value);
                }
                else
                {
                    logger.LogWarning("Updated Schedule [{ScheduleId}] - Hangfire trigger removed due to missing CRON or Date.", id);
                }
            }

            return Results.NoContent();
        })
        .WithTags("Schedules");
    }
}
