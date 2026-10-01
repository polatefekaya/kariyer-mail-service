using System.Diagnostics;
using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Features.DispatchEmail;
using Kariyer.Mail.Api.Features.Templates;
using Kariyer.Messaging.Contracts.Recruiting;
using MassTransit;
using Microsoft.Extensions.Options;

namespace Kariyer.Mail.Api.Features.Recruiting.InterviewInvited;

/// <summary>
/// The invitation the candidate receives when a company schedules an interview.
///
/// This is the only mail an invitation sends. The same commit also publishes
/// <c>ApplicationStageChangedEvent</c> with <c>ToStage = INTERVIEW</c>; nothing here or anywhere
/// else in this service may mail on that, or one invitation would arrive twice.
///
/// The accept/decline links are minted by the recruiting service, which owns
/// <c>confirmation_status</c>. They are signed, single-purpose and go only to the candidate — a
/// template that forwards them to anyone else hands that person the candidate's answer.
/// </summary>
internal sealed class InterviewInvitedConsumer : IConsumer<InterviewInvitedEvent>
{
    private readonly ILogger<InterviewInvitedConsumer> _logger;
    private readonly EmailTemplateSettings _templateSettings;
    private readonly ITemplateResolutionService _templateService;
    private readonly MailDbContext _dbContext;

    public InterviewInvitedConsumer(
        ILogger<InterviewInvitedConsumer> logger,
        IOptions<EmailTemplateSettings> templateOptions,
        ITemplateResolutionService templateService,
        MailDbContext dbContext)
    {
        _logger = logger;
        _templateSettings = templateOptions.Value;
        _templateService = templateService;
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<InterviewInvitedEvent> context)
    {
        InterviewInvitedEvent message = context.Message;

        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("ProcessInterviewInvitedEvent");
        activity?.SetTag("mail.event_type", "recruiting.interview.invited");
        activity?.SetTag("interview.uid", message.InterviewUid);
        activity?.SetTag("message.id", message.MessageId);

        _logger.LogInformation(
            "Processing Interview Invited event for {CandidateName} [{CandidateUid}] on {JobTitle}",
            message.CandidateName, message.CandidateUid, message.JobTitle);

        if (string.IsNullOrWhiteSpace(message.CandidateEmail))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "No Candidate Address");
            _logger.LogError(
                "Interview {InterviewUid} carried no candidate address; the invitation cannot be sent.",
                message.InterviewUid);
            return;
        }

        string slug = _templateSettings.InterviewInvitedTemplateSlug;
        activity?.SetTag("mail.template_slug", slug);

        if (string.IsNullOrWhiteSpace(slug))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Missing Template Slug Configuration");
            throw new InvalidOperationException("CRITICAL: InterviewInvitedTemplateSlug is missing in configuration.");
        }

        EmailTemplate? template = await _templateService.GetBySlugAsync(slug, context.CancellationToken);

        if (template == null)
        {
            DiagnosticsConfig.TemplateNotFoundCounter.Add(1, new KeyValuePair<string, object?>("slug", slug));
            activity?.SetStatus(ActivityStatusCode.Error, "Template Not Found");
            throw new Exception($"CRITICAL: Template with slug '{slug}' not found. Cannot send interview invitation to {message.CandidateEmail}.");
        }

        Dictionary<string, string> templateData = new()
        {
            { "CandidateName", message.CandidateName },
            { "CompanyName", message.CompanyName },
            { "JobTitle", message.JobTitle },
            { "InterviewDateTime", InterviewMailContent.DateTimeIn(message.StartsAt, message.TimeZone) },
            { "TimeZone", InterviewMailContent.ZoneLabel(message.TimeZone) },
            { "Duration", InterviewMailContent.Duration(message.DurationMinutes) },
            { "InterviewType", InterviewMailContent.TypeLabel(message.Type) },
            { "LocationLabel", InterviewMailContent.LocationLabel(message.Type) },
            { "Location", InterviewMailContent.Location(message.Type, message.VideoUrl, message.Location) },
            { "Message", message.CandidateMessage },
            { "InvitedByName", message.InvitedByName },
            { "AcceptUrl", message.AcceptUrl },
            { "DeclineUrl", message.DeclineUrl },
        };

        EmailTarget target = new(
            null, message.CandidateUid, message.CandidateEmail, template.SubjectTemplate, template.HtmlContent);

        _dbContext.EmailTargets.Add(target);
        await _dbContext.SaveChangesAsync(context.CancellationToken);

        DispatchEmailCommand dispatchCommand = new()
        {
            TargetId = target.Id,
            JobId = null,
            Email = message.CandidateEmail,
            Subject = template.SubjectTemplate,
            RawTemplate = template.HtmlContent,
            TemplateData = templateData
        };

        await context.Publish(dispatchCommand, context.CancellationToken);

        activity?.SetStatus(ActivityStatusCode.Ok);
        _logger.LogInformation(
            "Successfully dispatched interview invitation for {Email} [{InterviewUid}]",
            message.CandidateEmail, message.InterviewUid);
    }
}
