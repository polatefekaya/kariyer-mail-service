using Kariyer.Mail.Api.Common.Enums;

namespace Kariyer.Mail.Api.Common.Models;

/// <summary>
/// The candidate-facing decision mail an application is waiting to send — at most one per
/// application, keyed by its uid.
///
/// Why a row and not a delayed message: a recruiter who marks the wrong candidate hired and
/// corrects it a minute later must not have mailed anyone. Keeping the pending mail as one row
/// per application makes that a plain update — the newer move replaces or cancels whatever was
/// waiting — and makes redelivery harmless, because the same move lands on the same row.
///
/// <see cref="ChangedAt"/> is the move's own timestamp, not the time it arrived, so a stale
/// event delivered late (a retry, a replayed outbox) cannot overwrite a newer decision.
/// </summary>
public sealed class PendingStageMail
{
    public string ApplicationUid { get; private set; }

    /// <summary>The publisher's id for the move this row currently reflects.</summary>
    public string MessageId { get; private set; }

    public PendingStageMailStatus Status { get; private set; }

    /// <summary>The stage the move went to — OFFER, HIRED or REJECTED while pending.</summary>
    public string ToStage { get; private set; }

    /// <summary>
    /// The EmailTemplateSettings property naming the slot. Resolved to a template only when the
    /// mail comes due, so an edit made in the admin panel during the hold still applies.
    /// </summary>
    public string? SettingsKey { get; private set; }

    public string? RecipientUserId { get; private set; }
    public string? RecipientEmail { get; private set; }

    /// <summary>The template variables, as JSON — the same dictionary the dispatcher renders with.</summary>
    public string? TemplateData { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }
    public DateTime? DueAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>Postgres <c>xmin</c>; the dispatch job and a newer move must not both win.</summary>
    public uint Version { get; private set; }

    private PendingStageMail()
    {
        ApplicationUid = string.Empty;
        MessageId = string.Empty;
        ToStage = string.Empty;
    }

    public static PendingStageMail Hold(
        string applicationUid,
        string messageId,
        string toStage,
        DateTimeOffset changedAt,
        string settingsKey,
        string? recipientUserId,
        string recipientEmail,
        string templateData,
        DateTime dueAt)
    {
        PendingStageMail mail = new() { ApplicationUid = applicationUid };
        mail.Replace(messageId, toStage, changedAt, settingsKey, recipientUserId, recipientEmail, templateData, dueAt);
        return mail;
    }

    /// <summary>A newer move that should be mailed: whatever was waiting is replaced, hold restarts.</summary>
    public void Replace(
        string messageId,
        string toStage,
        DateTimeOffset changedAt,
        string settingsKey,
        string? recipientUserId,
        string recipientEmail,
        string templateData,
        DateTime dueAt)
    {
        MessageId = messageId;
        Status = PendingStageMailStatus.Pending;
        ToStage = toStage;
        ChangedAt = changedAt;
        SettingsKey = settingsKey;
        RecipientUserId = recipientUserId;
        RecipientEmail = recipientEmail;
        TemplateData = templateData;
        DueAt = dueAt;
        ErrorMessage = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// A newer move that is silent. Cancels a mail still waiting; a mail already dispatched stays
    /// recorded as dispatched — this only advances <see cref="ChangedAt"/> so older moves stay out.
    /// </summary>
    public void Supersede(string messageId, string toStage, DateTimeOffset changedAt)
    {
        if (Status == PendingStageMailStatus.Pending)
        {
            Status = PendingStageMailStatus.Cancelled;
        }

        MessageId = messageId;
        ToStage = toStage;
        ChangedAt = changedAt;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Dropped without a newer move — e.g. decision mails were switched off while it waited.</summary>
    public void Cancel(string reason)
    {
        Status = PendingStageMailStatus.Cancelled;
        ErrorMessage = reason;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkDispatched()
    {
        Status = PendingStageMailStatus.Dispatched;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkFailed(string error)
    {
        Status = PendingStageMailStatus.Failed;
        ErrorMessage = error;
        UpdatedAt = DateTime.UtcNow;
    }
}
