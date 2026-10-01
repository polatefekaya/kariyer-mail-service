using System.Diagnostics;
using System.Text.Json;
using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Messaging.Contracts.Recruiting;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kariyer.Mail.Api.Features.Recruiting.ApplicationStageChanged;

/// <summary>
/// Tells the candidate about an offer, a hire or a rejection — after a hold.
///
/// Nothing is sent from here. The move is written to <see cref="PendingStageMail"/> and
/// <see cref="PendingStageMailDispatchJob"/> sends it once the hold has passed, unless a newer
/// move for the same application has replaced or cancelled it in the meantime. That is how a
/// recruiter's misclick and its correction reach the candidate as nothing at all.
///
/// Every move is consumed, including the silent ones: a silent move is exactly what cancels a
/// mail still waiting.
/// </summary>
internal sealed class ApplicationStageChangedConsumer : IConsumer<ApplicationStageChangedEvent>
{
    private readonly ILogger<ApplicationStageChangedConsumer> _logger;
    private readonly RecruitingMailSettings _settings;
    private readonly MailDbContext _dbContext;

    public ApplicationStageChangedConsumer(
        ILogger<ApplicationStageChangedConsumer> logger,
        IOptions<RecruitingMailSettings> settings,
        MailDbContext dbContext)
    {
        _logger = logger;
        _settings = settings.Value;
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<ApplicationStageChangedEvent> context)
    {
        ApplicationStageChangedEvent message = context.Message;

        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("ProcessApplicationStageChangedEvent");
        activity?.SetTag("mail.event_type", "recruiting.application.stage_changed");
        activity?.SetTag("application.uid", message.ApplicationUid);
        activity?.SetTag("stage.from", message.FromStage);
        activity?.SetTag("stage.to", message.ToStage);
        activity?.SetTag("message.id", message.MessageId);

        PendingStageMail? existing = await _dbContext.PendingStageMails
            .FirstOrDefaultAsync(m => m.ApplicationUid == message.ApplicationUid, context.CancellationToken);

        // The move's own time decides, not arrival order: a redelivered or replayed event must
        // never overwrite a decision made after it.
        if (existing != null && message.ChangedAt <= existing.ChangedAt)
        {
            _logger.LogInformation(
                "Ignoring stale or duplicate stage move {From} → {To} for application {ApplicationUid}.",
                message.FromStage, message.ToStage, message.ApplicationUid);
            activity?.SetStatus(ActivityStatusCode.Ok, "Stale move.");
            return;
        }

        string? settingsKey = StageMailPolicy.SettingsKeyFor(message.FromStage, message.ToStage);

        if (settingsKey != null && string.IsNullOrWhiteSpace(message.CandidateEmail))
        {
            // A publisher older than contracts 1.4.0 sends no address. Treated as silent so the
            // move still cancels anything waiting — never as a reason to look the address up.
            _logger.LogError(
                "Stage move to {To} for application {ApplicationUid} carried no candidate address; the mail cannot be sent.",
                message.ToStage, message.ApplicationUid);
            settingsKey = null;
        }

        if (settingsKey == null)
        {
            if (existing != null)
            {
                existing.Supersede(message.MessageId, message.ToStage, message.ChangedAt);
                await _dbContext.SaveChangesAsync(context.CancellationToken);
            }

            activity?.SetStatus(ActivityStatusCode.Ok, "Silent move.");
            return;
        }

        Dictionary<string, string> templateData = new()
        {
            { "CandidateName", message.CandidateName },
            { "CompanyName", message.CompanyName },
            { "JobTitle", message.JobTitle },
        };

        if (settingsKey == nameof(EmailTemplateSettings.ApplicationRejectedTemplateSlug))
        {
            templateData["AfterInterview"] = StageMailPolicy.IsAfterInterview(message.FromStage) ? "true" : "false";
        }

        string serialized = JsonSerializer.Serialize(templateData);
        DateTime dueAt = message.ChangedAt.UtcDateTime.AddMinutes(_settings.StageMailHoldMinutes);

        if (existing == null)
        {
            _dbContext.PendingStageMails.Add(PendingStageMail.Hold(
                message.ApplicationUid, message.MessageId, message.ToStage, message.ChangedAt,
                settingsKey, message.CandidateUid, message.CandidateEmail, serialized, dueAt));
        }
        else
        {
            existing.Replace(
                message.MessageId, message.ToStage, message.ChangedAt,
                settingsKey, message.CandidateUid, message.CandidateEmail, serialized, dueAt);
        }

        await _dbContext.SaveChangesAsync(context.CancellationToken);

        activity?.SetStatus(ActivityStatusCode.Ok);
        _logger.LogInformation(
            "Held {To} mail for application {ApplicationUid} until {DueAt:O}.",
            message.ToStage, message.ApplicationUid, dueAt);
    }
}
