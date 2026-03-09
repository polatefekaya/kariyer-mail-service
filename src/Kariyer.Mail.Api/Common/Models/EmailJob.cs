using System.Text.Json;
using Kariyer.Mail.Api.Common.Enums;

namespace Kariyer.Mail.Api.Common.Models;

public sealed class EmailJob
{
    public Ulid Id { get; init; } = Ulid.NewUlid();
    public string? CreatedByAdminId { get; init; }
    
    public EmailJobType JobType { get; init; }
    
    public Ulid? TemplateId { get; init; }
    public EmailTemplate? Template { get; private set; }

    public string? Subject { get; init; }
    public string? BodyTemplate { get; init; }
    
    public JsonDocument Payload { get; init; }
    
    public EmailJobStatus Status { get; private set; } = EmailJobStatus.Pending;
    public string? ErrorMessage { get; private set; }
    
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; private set; }
    
    public Ulid? ScheduleId { get; init; }

    private EmailJob() 
    { 
        Payload = JsonDocument.Parse("{}");
    }

    public EmailJob(string adminId, EmailJobType type, Ulid? templateId, string? subject, string? bodyTemplate, JsonDocument payload, Ulid? scheduleId = null)
    {
        CreatedByAdminId = adminId;
        JobType = type;
        TemplateId = templateId;
        Subject = subject;
        BodyTemplate = bodyTemplate;
        Payload = payload;
        ScheduleId = scheduleId;
    }

    public void MarkAsResolving() => Status = EmailJobStatus.Resolving;

    public void MarkAsQueuing() => Status = EmailJobStatus.Queuing;

    public void MarkAsCancelled(string reason) {
        Status = EmailJobStatus.Cancelled;
        ErrorMessage = reason;
    }
    public void MarkAsCompleted()
    {
        Status = EmailJobStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    public void MarkAsFailed(string error)
    {
        Status = EmailJobStatus.Failed;
        CompletedAt = DateTime.UtcNow;
        ErrorMessage = error;
    }
}