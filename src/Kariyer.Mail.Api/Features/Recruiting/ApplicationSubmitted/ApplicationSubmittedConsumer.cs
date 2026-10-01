using System.Diagnostics;
using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Features.Templates;
using Kariyer.Messaging.Contracts.Recruiting;
using MassTransit;
using Microsoft.Extensions.Options;

namespace Kariyer.Mail.Api.Features.Recruiting.ApplicationSubmitted;

/// <summary>
/// A new application: the candidate gets "başvurunuz alındı", the company gets "yeni başvuru".
///
/// Both mails come from the one event so they cannot disagree about what was applied to. Either
/// address may be missing — the publisher resolves both — and a missing one only skips its own
/// mail; the candidate's acknowledgement does not depend on the company having an address.
/// </summary>
internal sealed class ApplicationSubmittedConsumer : IConsumer<ApplicationSubmittedEvent>
{
    private readonly ILogger<ApplicationSubmittedConsumer> _logger;
    private readonly EmailTemplateSettings _templateSettings;
    private readonly ITemplateResolutionService _templateService;
    private readonly MailDbContext _dbContext;

    public ApplicationSubmittedConsumer(
        ILogger<ApplicationSubmittedConsumer> logger,
        IOptions<EmailTemplateSettings> templateOptions,
        ITemplateResolutionService templateService,
        MailDbContext dbContext)
    {
        _logger = logger;
        _templateSettings = templateOptions.Value;
        _templateService = templateService;
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<ApplicationSubmittedEvent> context)
    {
        ApplicationSubmittedEvent message = context.Message;

        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("ProcessApplicationSubmittedEvent");
        activity?.SetTag("mail.event_type", "recruiting.application.submitted");
        activity?.SetTag("application.uid", message.ApplicationUid);
        activity?.SetTag("message.id", message.MessageId);

        _logger.LogInformation(
            "Processing Application Submitted event for {CandidateName} [{CandidateUid}] on {JobTitle}",
            message.CandidateName, message.CandidateUid, message.JobTitle);

        string submittedAt = InterviewMailContent.DateTimeIn(message.SubmittedAt, RecruitingMail.DefaultTimeZone);

        if (string.IsNullOrWhiteSpace(message.CandidateEmail))
        {
            _logger.LogError(
                "Application {ApplicationUid} carried no candidate address; the acknowledgement cannot be sent.",
                message.ApplicationUid);
        }
        else
        {
            await RecruitingMail.QueueAsync(
                _dbContext, context, _templateService,
                _templateSettings.ApplicationSubmittedTemplateSlug,
                nameof(EmailTemplateSettings.ApplicationSubmittedTemplateSlug),
                message.CandidateUid,
                message.CandidateEmail,
                new Dictionary<string, string>
                {
                    { "CandidateName", message.CandidateName },
                    { "CompanyName", message.CompanyName },
                    { "JobTitle", message.JobTitle },
                    { "SubmittedAt", submittedAt },
                    { "ApplicationsUrl", message.CandidateApplicationsUrl },
                },
                context.CancellationToken);
        }

        if (string.IsNullOrWhiteSpace(message.CompanyEmail))
        {
            _logger.LogWarning(
                "Application {ApplicationUid} carried no company address; the company is not notified.",
                message.ApplicationUid);
        }
        else
        {
            await RecruitingMail.QueueAsync(
                _dbContext, context, _templateService,
                _templateSettings.ApplicationSubmittedCompanyTemplateSlug,
                nameof(EmailTemplateSettings.ApplicationSubmittedCompanyTemplateSlug),
                message.CompanyUid,
                message.CompanyEmail,
                new Dictionary<string, string>
                {
                    { "CompanyName", message.CompanyName },
                    { "CandidateName", message.CandidateName },
                    { "JobTitle", message.JobTitle },
                    { "SubmittedAt", submittedAt },
                    { "IsQuickApply", message.IsQuickApply ? "true" : "false" },
                    { "ReviewUrl", message.CompanyReviewUrl },
                },
                context.CancellationToken);
        }

        activity?.SetStatus(ActivityStatusCode.Ok);
    }
}
