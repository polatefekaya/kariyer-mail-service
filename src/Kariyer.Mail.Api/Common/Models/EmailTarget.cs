using Kariyer.Mail.Api.Common.Enums;

namespace Kariyer.Mail.Api.Common.Models;

public sealed class EmailTarget
{
    public Ulid Id { get; init; } = Ulid.NewUlid();

    public Ulid? JobId { get; init; } 
    public string? RecipientUserId { get; init; }
    
    public string RecipientEmail { get; init; }
    public string Subject { get; init; }
    public string Body { get; init; }
    
    public TargetStatus Status { get; private set; } = TargetStatus.Pending;
    public string? ErrorMessage { get; private set; }
    
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; private set; }

    private EmailTarget() 
    { 
        RecipientEmail = string.Empty;
        Subject = string.Empty;
        Body = string.Empty;
    }

    public EmailTarget(Ulid? jobId, string? recipientUserId, string recipientEmail, string subject, string body)
    {
        JobId = jobId;
        RecipientUserId = recipientUserId;
        RecipientEmail = recipientEmail;
        Subject = subject;
        Body = body;
    }
    
    public void MarkAsQueued()
    {
        Status = TargetStatus.Queued;
    }

    public void MarkAsSent()
    {
        Status = TargetStatus.Sent;
        ProcessedAt = DateTime.UtcNow;
        ErrorMessage = null;
    }

    public void MarkAsFailed(string error)
    {
        Status = TargetStatus.Failed;
        ProcessedAt = DateTime.UtcNow;
        ErrorMessage = error;
    }
}