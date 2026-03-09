using System.Diagnostics;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Features.BulkEmail;
using MassTransit;
using Microsoft.EntityFrameworkCore;
namespace Kariyer.Mail.Api.Features.Schedules.Execution;

public sealed class ScheduleTriggerInvoker
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduleTriggerInvoker> _logger;

    public ScheduleTriggerInvoker(IServiceScopeFactory scopeFactory, ILogger<ScheduleTriggerInvoker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task ExecuteScheduleAsync(Ulid scheduleId)
    {
        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("ExecuteScheduledJob");
        activity?.SetTag("schedule.id", scheduleId);

        _logger.LogInformation("Hangfire triggered scheduled execution for Blueprint [{ScheduleId}].", scheduleId);
        
        using IServiceScope scope = _scopeFactory.CreateScope();
        MailDbContext dbContext = scope.ServiceProvider.GetRequiredService<MailDbContext>();
        IPublishEndpoint publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        try
        {
            EmailJobSchedule? scheduleBlueprint = await dbContext.EmailJobSchedules
                .FirstOrDefaultAsync(s => s.Id == scheduleId && s.IsActive);

            if (scheduleBlueprint == null)
            {
                _logger.LogWarning("Schedule [{ScheduleId}] is inactive or was hard-deleted. Halting Hangfire execution.", scheduleId);
                activity?.SetStatus(ActivityStatusCode.Ok, "Schedule inactive.");
                return;
            }

            activity?.SetTag("job.type", scheduleBlueprint.JobType.ToString());

            EmailJob freshJobExecution = new EmailJob(
                adminId: scheduleBlueprint.AdminId,
                type: scheduleBlueprint.JobType,
                templateId: scheduleBlueprint.TemplateId,
                subject: scheduleBlueprint.Subject,
                bodyTemplate: scheduleBlueprint.BodyTemplate,
                payload: scheduleBlueprint.Filters,
                scheduleId: scheduleBlueprint.Id
            );

            await dbContext.EmailJobs.AddAsync(freshJobExecution);

            StartBulkEmailJobCommand command = new()
            {
                JobId = freshJobExecution.Id,
                TemplateId = scheduleBlueprint.TemplateId
            };
            
            await publishEndpoint.Publish(command);

            await dbContext.SaveChangesAsync();

            KeyValuePair<string, object?> metricTag = new KeyValuePair<string, object?>("schedule_id", scheduleId.ToString());
            DiagnosticsConfig.ScheduledJobsTriggeredCounter.Add(1, metricTag);

            activity?.SetStatus(ActivityStatusCode.Ok);
            
            _logger.LogInformation("Successfully stamped Job [{JobId}] from Schedule [{ScheduleId}]. Handed off to MassTransit.", 
                freshJobExecution.Id, scheduleId);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);

            _logger.LogError(ex, "Catastrophic failure while attempting to execute Schedule [{ScheduleId}].", scheduleId);
            
            throw; 
        }
    }
}