using System.Diagnostics;
using Hangfire;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;
using Kariyer.Mail.Api.Features.Schedules.Execution;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Schedules.CreateSchedule;

internal sealed class CreateScheduleEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("schedules", async (
            CreateScheduleRequest request,
            MailDbContext dbContext,
            IBackgroundJobClient backgroundJobs,
            IRecurringJobManager recurringJobs,
            IConnectionMultiplexer multiplexer,
            ILogger<CreateScheduleEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("CreateSchedule");

            string adminId = Ulid.NewUlid().ToString(); 

            EmailJobSchedule schedule = new (
                request.Name, adminId, request.JobType, request.TemplateId, 
                request.Subject, request.BodyTemplate, request.Filters,
                request.IsRecurring, request.CronExpression, request.OneTimeExecuteAt
            );

            await dbContext.EmailJobSchedules.AddAsync(schedule, ct);
            await dbContext.SaveChangesAsync(ct);
            
            activity?.SetTag("schedule.id", schedule.Id.ToString());
            activity?.SetTag("schedule.is_recurring", schedule.IsRecurring);
            
            IDatabase garnet = multiplexer.GetDatabase();
            await garnet.KeyDeleteAsync("schedules:all:inactive_false");
            await garnet.KeyDeleteAsync("schedules:all:inactive_true");

            if (schedule.IsRecurring && !string.IsNullOrWhiteSpace(schedule.CronExpression))
            {
                recurringJobs.AddOrUpdate<ScheduleTriggerInvoker>(
                    recurringJobId: schedule.Id.ToString(),
                    methodCall: invoker => invoker.ExecuteScheduleAsync(schedule.Id),
                    cronExpression: schedule.CronExpression);
                    
                logger.LogInformation("Created recurring schedule [{ScheduleId}] with CRON: {CronExpression}", schedule.Id, schedule.CronExpression);
            }
            else if (!schedule.IsRecurring && schedule.OneTimeExecuteAt.HasValue)
            {
                backgroundJobs.Schedule<ScheduleTriggerInvoker>(
                    methodCall: invoker => invoker.ExecuteScheduleAsync(schedule.Id),
                    enqueueAt: schedule.OneTimeExecuteAt.Value);
                    
                logger.LogInformation("Created one-time schedule [{ScheduleId}] set to execute at: {ExecuteAt}", schedule.Id, schedule.OneTimeExecuteAt.Value);
            }
            else 
            {
                logger.LogWarning("Schedule [{ScheduleId}] was saved to DB, but no Hangfire trigger was configured due to missing CRON or Execution Date.", schedule.Id);
            }

            return Results.Ok(new { ScheduleId = schedule.Id });
        })
        .WithTags("Schedules");
    }
}