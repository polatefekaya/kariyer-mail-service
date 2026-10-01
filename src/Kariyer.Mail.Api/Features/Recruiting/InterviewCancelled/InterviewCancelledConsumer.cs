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

namespace Kariyer.Mail.Api.Features.Recruiting.InterviewCancelled;

/// <summary>
/// Tells the candidate that a scheduled interview has been called off.
///
/// Cancellation only. A candidate who did not turn up is recorded as NO_SHOW and publishes
/// nothing — mailing someone about a meeting they know they missed is a reproach, not a
/// notification. A cancellation also does not move the application's stage, so this mail must not
/// read as a rejection.
/// </summary>
internal sealed class InterviewCancelledConsumer : IConsumer<InterviewCancelledEvent>
{
    private readonly ILogger<InterviewCancelledConsumer> _logger;
    private readonly EmailTemplateSettings _templateSettings;
    private readonly ITemplateResolutionService _templateService;
    private readonly MailDbContext _dbContext;

    public InterviewCancelledConsumer(
        ILogger<InterviewCancelledConsumer> logger,
        IOptions<EmailTemplateSettings> templateOptions,
        ITemplateResolutionService templateService,
        MailDbContext dbContext)
    {
        _logger = logger;
        _templateSettings = templateOptions.Value;
        _templateService = templateService;
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<InterviewCancelledEvent> context)
    {
        InterviewCancelledEvent message = context.Message;

        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("ProcessInterviewCancelledEvent");
        activity?.SetTag("mail.event_type", "recruiting.interview.cancelled");
        activity?.SetTag("interview.uid", message.InterviewUid);
        activity?.SetTag("message.id", message.MessageId);

        _logger.LogInformation(
            "Processing Interview Cancelled event for {CandidateName} [{CandidateUid}] on {JobTitle}",
            message.CandidateName, message.CandidateUid, message.JobTitle);

        if (string.IsNullOrWhiteSpace(message.CandidateEmail))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "No Candidate Address");
            _logger.LogError(
                "Interview {InterviewUid} carried no candidate address; the cancellation cannot be sent.",
                message.InterviewUid);
            return;
        }

        string slug = _templateSettings.InterviewCancelledTemplateSlug;
        activity?.SetTag("mail.template_slug", slug);

        if (string.IsNullOrWhiteSpace(slug))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Missing Template Slug Configuration");
            throw new InvalidOperationException("CRITICAL: InterviewCancelledTemplateSlug is missing in configuration.");
        }

        EmailTemplate? template = await _templateService.GetBySlugAsync(slug, context.CancellationToken);

        if (template == null)
        {
            DiagnosticsConfig.TemplateNotFoundCounter.Add(1, new KeyValuePair<string, object?>("slug", slug));
            activity?.SetStatus(ActivityStatusCode.Error, "Template Not Found");
            throw new Exception($"CRITICAL: Template with slug '{slug}' not found. Cannot send interview cancellation to {message.CandidateEmail}.");
        }

        Dictionary<string, string> templateData = new()
        {
            { "CandidateName", message.CandidateName },
            { "CompanyName", message.CompanyName },
            { "JobTitle", message.JobTitle },
            { "InterviewDateTime", InterviewMailContent.DateTimeIn(message.StartsAt, message.TimeZone) },
            { "TimeZone", InterviewMailContent.ZoneLabel(message.TimeZone) },
            { "Message", message.CandidateMessage },
            { "CancelledByName", message.CancelledByName },
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
            "Successfully dispatched interview cancellation for {Email} [{InterviewUid}]",
            message.CandidateEmail, message.InterviewUid);
    }
}
