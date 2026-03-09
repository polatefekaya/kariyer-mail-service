using Hangfire;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
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
            CancellationToken ct) =>
        {
            EmailJobSchedule? schedule = await dbContext.EmailJobSchedules
                .FirstOrDefaultAsync(s => s.Id == id && s.IsActive, ct);

            if (schedule == null) return Results.NotFound();

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
                    methodCall: invoker => invoker.ExecuteScheduleAsync(schedule.Id),
                    cronExpression: schedule.CronExpression);
            }
            else
            {
                recurringJobs.RemoveIfExists(hangfireJobId);

                if (schedule.OneTimeExecuteAt.HasValue)
                {
                    backgroundJobs.Schedule<ScheduleTriggerInvoker>(
                        methodCall: invoker => invoker.ExecuteScheduleAsync(schedule.Id),
                        enqueueAt: schedule.OneTimeExecuteAt.Value);
                }
            }

            return Results.NoContent();
        })
        .WithTags("Schedules");
    }
}