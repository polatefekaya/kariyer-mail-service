using System.Diagnostics;
using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Features.Templates;
using Kariyer.Messaging.Contracts.Recruiting;
using MassTransit;
using Microsoft.Extensions.Options;

namespace Kariyer.Mail.Api.Features.Recruiting.InterviewAnswered;

/// <summary>
/// Tells the company side that the candidate accepted or declined an interview — everyone the
/// recruiting service listed: whoever invited plus the participants. Each recipient gets their
/// own mail so the greeting can name them.
/// </summary>
internal sealed class InterviewAnsweredConsumer : IConsumer<InterviewAnsweredEvent>
{
    private const string Accepted = "ACCEPTED";
    private const string Declined = "DECLINED";

    private readonly ILogger<InterviewAnsweredConsumer> _logger;
    private readonly EmailTemplateSettings _templateSettings;
    private readonly ITemplateResolutionService _templateService;
    private readonly MailDbContext _dbContext;

    public InterviewAnsweredConsumer(
        ILogger<InterviewAnsweredConsumer> logger,
        IOptions<EmailTemplateSettings> templateOptions,
        ITemplateResolutionService templateService,
        MailDbContext dbContext)
    {
        _logger = logger;
        _templateSettings = templateOptions.Value;
        _templateService = templateService;
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<InterviewAnsweredEvent> context)
    {
        InterviewAnsweredEvent message = context.Message;

        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("ProcessInterviewAnsweredEvent");
        activity?.SetTag("mail.event_type", "recruiting.interview.answered");
        activity?.SetTag("interview.uid", message.InterviewUid);
        activity?.SetTag("interview.answer", message.Answer);
        activity?.SetTag("message.id", message.MessageId);

        (string slug, string settingsKey) = message.Answer switch
        {
            Accepted => (_templateSettings.InterviewAcceptedTemplateSlug, nameof(EmailTemplateSettings.InterviewAcceptedTemplateSlug)),
            Declined => (_templateSettings.InterviewDeclinedTemplateSlug, nameof(EmailTemplateSettings.InterviewDeclinedTemplateSlug)),
            _ => (string.Empty, string.Empty),
        };

        if (settingsKey.Length == 0)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Unknown Answer");
            _logger.LogError(
                "Interview {InterviewUid} carried an unknown answer '{Answer}'; nothing is sent.",
                message.InterviewUid, message.Answer);
            return;
        }

        string interviewDateTime = InterviewMailContent.DateTimeIn(message.StartsAt, message.TimeZone);
        int sent = 0;

        foreach (InterviewParticipantContract recipient in message.Recipients
                     .Where(r => !string.IsNullOrWhiteSpace(r.Email))
                     .DistinctBy(r => r.Email.Trim().ToLowerInvariant()))
        {
            await RecruitingMail.QueueAsync(
                _dbContext, context, _templateService,
                slug, settingsKey,
                null,
                recipient.Email,
                new Dictionary<string, string>
                {
                    { "RecipientName", recipient.Name },
                    { "CandidateName", message.CandidateName },
                    { "CompanyName", message.CompanyName },
                    { "JobTitle", message.JobTitle },
                    { "InterviewDateTime", interviewDateTime },
                    { "TimeZone", InterviewMailContent.ZoneLabel(message.TimeZone) },
                    { "InterviewType", InterviewMailContent.TypeLabel(message.Type) },
                    { "ReviewUrl", message.CompanyReviewUrl },
                },
                context.CancellationToken);

            sent++;
        }

        activity?.SetTag("mail.recipients", sent);
        activity?.SetStatus(ActivityStatusCode.Ok);
        _logger.LogInformation(
            "Dispatched {Answer} notice for interview {InterviewUid} to {Count} recipient(s)",
            message.Answer, message.InterviewUid, sent);
    }
}
