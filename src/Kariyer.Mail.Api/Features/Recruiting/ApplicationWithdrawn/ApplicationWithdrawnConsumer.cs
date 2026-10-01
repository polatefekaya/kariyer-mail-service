using System.Diagnostics;
using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Features.Templates;
using Kariyer.Messaging.Contracts.Recruiting;
using MassTransit;
using Microsoft.Extensions.Options;

namespace Kariyer.Mail.Api.Features.Recruiting.ApplicationWithdrawn;

/// <summary>
/// Tells the company a candidate withdrew, so nobody books an interview with someone who has
/// already walked away. The candidate is not mailed: they took the action and saw it on screen.
/// </summary>
internal sealed class ApplicationWithdrawnConsumer : IConsumer<ApplicationWithdrawnEvent>
{
    private readonly ILogger<ApplicationWithdrawnConsumer> _logger;
    private readonly EmailTemplateSettings _templateSettings;
    private readonly ITemplateResolutionService _templateService;
    private readonly MailDbContext _dbContext;

    public ApplicationWithdrawnConsumer(
        ILogger<ApplicationWithdrawnConsumer> logger,
        IOptions<EmailTemplateSettings> templateOptions,
        ITemplateResolutionService templateService,
        MailDbContext dbContext)
    {
        _logger = logger;
        _templateSettings = templateOptions.Value;
        _templateService = templateService;
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<ApplicationWithdrawnEvent> context)
    {
        ApplicationWithdrawnEvent message = context.Message;

        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("ProcessApplicationWithdrawnEvent");
        activity?.SetTag("mail.event_type", "recruiting.application.withdrawn");
        activity?.SetTag("application.uid", message.ApplicationUid);
        activity?.SetTag("message.id", message.MessageId);

        if (string.IsNullOrWhiteSpace(message.CompanyEmail))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "No Company Address");
            _logger.LogError(
                "Withdrawal of application {ApplicationUid} carried no company address; the company is not notified.",
                message.ApplicationUid);
            return;
        }

        await RecruitingMail.QueueAsync(
            _dbContext, context, _templateService,
            _templateSettings.ApplicationWithdrawnTemplateSlug,
            nameof(EmailTemplateSettings.ApplicationWithdrawnTemplateSlug),
            message.CompanyUid,
            message.CompanyEmail,
            new Dictionary<string, string>
            {
                { "CompanyName", message.CompanyName },
                { "CandidateName", message.CandidateName },
                { "JobTitle", message.JobTitle },
                { "WithdrawnAt", InterviewMailContent.DateTimeIn(message.WithdrawnAt, RecruitingMail.DefaultTimeZone) },
                { "ReviewUrl", message.CompanyReviewUrl },
            },
            context.CancellationToken);

        activity?.SetStatus(ActivityStatusCode.Ok);
        _logger.LogInformation(
            "Successfully dispatched withdrawal notice for application {ApplicationUid} to {Email}",
            message.ApplicationUid, message.CompanyEmail);
    }
}
