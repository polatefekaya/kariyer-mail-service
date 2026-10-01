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

namespace Kariyer.Mail.Api.Features.Recruiting.InterviewRescheduled;

/// <summary>
/// Tells the candidate that an interview they were already invited to has moved.
///
/// The publisher only emits this when something the candidate can see changed, so anything that
/// arrives here is worth an e-mail. It carries the previous start as well as the new one: a
/// candidate who receives a bare new time cannot tell a change from a duplicate invitation.
/// </summary>
internal sealed class InterviewRescheduledConsumer : IConsumer<InterviewRescheduledEvent>
{
    private readonly ILogger<InterviewRescheduledConsumer> _logger;
    private readonly EmailTemplateSettings _templateSettings;
    private readonly ITemplateResolutionService _templateService;
    private readonly MailDbContext _dbContext;

    public InterviewRescheduledConsumer(
        ILogger<InterviewRescheduledConsumer> logger,
        IOptions<EmailTemplateSettings> templateOptions,
        ITemplateResolutionService templateService,
        MailDbContext dbContext)
    {
        _logger = logger;
        _templateSettings = templateOptions.Value;
        _templateService = templateService;
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<InterviewRescheduledEvent> context)
    {
        InterviewRescheduledEvent message = context.Message;

        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("ProcessInterviewRescheduledEvent");
        activity?.SetTag("mail.event_type", "recruiting.interview.rescheduled");
        activity?.SetTag("interview.uid", message.InterviewUid);
        activity?.SetTag("message.id", message.MessageId);

        _logger.LogInformation(
            "Processing Interview Rescheduled event for {CandidateName} [{CandidateUid}] on {JobTitle}",
            message.CandidateName, message.CandidateUid, message.JobTitle);

        if (string.IsNullOrWhiteSpace(message.CandidateEmail))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "No Candidate Address");
            _logger.LogError(
                "Interview {InterviewUid} carried no candidate address; the change cannot be sent.",
                message.InterviewUid);
            return;
        }

        string slug = _templateSettings.InterviewRescheduledTemplateSlug;
        activity?.SetTag("mail.template_slug", slug);

        if (string.IsNullOrWhiteSpace(slug))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Missing Template Slug Configuration");
            throw new InvalidOperationException("CRITICAL: InterviewRescheduledTemplateSlug is missing in configuration.");
        }

        EmailTemplate? template = await _templateService.GetBySlugAsync(slug, context.CancellationToken);

        if (template == null)
        {
            DiagnosticsConfig.TemplateNotFoundCounter.Add(1, new KeyValuePair<string, object?>("slug", slug));
            activity?.SetStatus(ActivityStatusCode.Error, "Template Not Found");
            throw new Exception($"CRITICAL: Template with slug '{slug}' not found. Cannot send interview change to {message.CandidateEmail}.");
        }

        Dictionary<string, string> templateData = new()
        {
            { "CandidateName", message.CandidateName },
            { "CompanyName", message.CompanyName },
            { "JobTitle", message.JobTitle },
            { "PreviousDateTime", InterviewMailContent.DateTimeIn(message.PreviousStartsAt, message.TimeZone) },
            { "InterviewDateTime", InterviewMailContent.DateTimeIn(message.StartsAt, message.TimeZone) },
            { "TimeZone", InterviewMailContent.ZoneLabel(message.TimeZone) },
            { "Duration", InterviewMailContent.Duration(message.DurationMinutes) },
            { "InterviewType", InterviewMailContent.TypeLabel(message.Type) },
            { "LocationLabel", InterviewMailContent.LocationLabel(message.Type) },
            { "Location", InterviewMailContent.Location(message.Type, message.VideoUrl, message.Location) },
            { "Message", message.CandidateMessage },
            { "ChangedByName", message.RescheduledByName },
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
            "Successfully dispatched interview change for {Email} [{InterviewUid}]",
            message.CandidateEmail, message.InterviewUid);
    }
}
