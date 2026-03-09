using System.Text.Json;
using Kariyer.Mail.Api.Common.Enums;

namespace Kariyer.Mail.Api.Common.Models;

public sealed class EmailJobSchedule
{
    public Ulid Id { get; private set; }
    public string Name { get; private set; }
    public string AdminId { get; private set; }
    
    public EmailJobType JobType { get; private set; }
    public Ulid? TemplateId { get; private set; }
    public string? Subject { get; private set; }
    public string? BodyTemplate { get; private set; }
    public JsonDocument Filters { get; private set; }

    public bool IsRecurring { get; private set; }
    public string? CronExpression { get; private set; }
    public DateTimeOffset? OneTimeExecuteAt { get; private set; }
    
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public EmailJobSchedule(
        string name, string adminId, EmailJobType jobType, Ulid? templateId, 
        string? subject, string? bodyTemplate, JsonDocument filters,
        bool isRecurring, string? cronExpression, DateTimeOffset? oneTimeExecuteAt)
    {
        Id = Ulid.NewUlid();
        Name = name;
        AdminId = adminId;
        JobType = jobType;
        TemplateId = templateId;
        Subject = subject;
        BodyTemplate = bodyTemplate;
        Filters = filters;
        IsRecurring = isRecurring;
        CronExpression = cronExpression;
        OneTimeExecuteAt = oneTimeExecuteAt;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    public void Update(
        string name, Ulid? templateId, string? subject, string? bodyTemplate, 
        JsonDocument filters, bool isRecurring, string? cronExpression, DateTimeOffset? oneTimeExecuteAt)
    {
        Name = name;
        TemplateId = templateId;
        Subject = subject;
        BodyTemplate = bodyTemplate;
        Filters = filters;
        IsRecurring = isRecurring;
        CronExpression = cronExpression;
        OneTimeExecuteAt = oneTimeExecuteAt;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate() 
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }
}