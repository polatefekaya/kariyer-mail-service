using System.Diagnostics;
using System.Globalization;
using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Features.DispatchEmail;
using Kariyer.Mail.Api.Features.Templates;
using MassTransit;
using Microsoft.Extensions.Options;

namespace Kariyer.Mail.Api.Features.JobAlert;

/// <summary>
/// Sends the "new jobs match your preferences" email.
///
/// Same shape as the Account consumers: resolve the configured slug, fail loudly if it or
/// the template is missing, write an EmailTarget, publish a DispatchEmailCommand.
///
/// The one thing that differs is the audience. Every other consumer here mails someone
/// about their own account — a transactional message nobody has to consent to. This one is
/// a standing subscription, so the PUBLISHER filters on
/// <c>ticari_elektronik_ileti_accepted</c> before emitting, and every message carries an
/// unsubscribe link. Do not reuse this consumer for an audience assembled elsewhere.
/// </summary>
internal sealed class JobAlertReadyConsumer : IConsumer<JobAlertReadyEvent>
{
    private readonly ILogger<JobAlertReadyConsumer> _logger;
    private readonly EmailTemplateSettings _templateSettings;
    private readonly ITemplateResolutionService _templateService;
    private readonly MailDbContext _dbContext;

    public JobAlertReadyConsumer(
        ILogger<JobAlertReadyConsumer> logger,
        IOptions<EmailTemplateSettings> templateOptions,
        ITemplateResolutionService templateService,
        MailDbContext dbContext)
    {
        _logger = logger;
        _templateSettings = templateOptions.Value;
        _templateService = templateService;
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<JobAlertReadyEvent> context)
    {
        JobAlertReadyEvent message = context.Message;

        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("ProcessJobAlertReadyEvent");
        activity?.SetTag("mail.event_type", "job.alert.ready");
        activity?.SetTag("account.uid", message.Uid);
        activity?.SetTag("message.id", message.MessageId);
        activity?.SetTag("job_alert.job_count", message.JobCount);

        _logger.LogInformation(
            "Processing Job Alert Ready event for {FullName} [{Uid}] with {JobCount} matches",
            message.FullName, message.Uid, message.JobCount);

        // A digest with nothing in it is not an email. The publisher does not cut an empty
        // batch, so this only fires if something upstream regressed — dropping it is right,
        // and far better than sending "0 new jobs".
        if (message.JobCount <= 0)
        {
            activity?.SetStatus(ActivityStatusCode.Ok, "Empty digest, nothing to send");
            _logger.LogWarning(
                "Job Alert event for {Uid} carried no matches; nothing sent.", message.Uid);
            return;
        }

        string slug = _templateSettings.JobAlertReadyTemplateSlug;
        activity?.SetTag("mail.template_slug", slug);
        if (string.IsNullOrWhiteSpace(slug))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Missing Template Slug Configuration");
            throw new InvalidOperationException("CRITICAL: JobAlertReadyTemplateSlug is missing in configuration.");
        }

        EmailTemplate? template = await _templateService.GetBySlugAsync(slug, context.CancellationToken);

        if (template == null)
        {
            DiagnosticsConfig.TemplateNotFoundCounter.Add(1, new KeyValuePair<string, object?>("slug", slug));
            activity?.SetStatus(ActivityStatusCode.Error, "Template Not Found");
            throw new Exception($"CRITICAL: Template with slug '{slug}' not found. Cannot send Job Alert email to {message.Email}.");
        }

        Dictionary<string, string> templateData = new()
        {
            { "FullName", message.FullName },
            // Rendered by the template, so a Turkish reader sees "3 yeni ilan" without the
            // template having to know how to pluralise a raw integer.
            { "JobCount", message.JobCount.ToString(CultureInfo.InvariantCulture) },
            { "AlertUrl", message.AlertUrl },
            // Falls back to the alerts page rather than rendering an empty href: an
            // unsubscribe link that goes nowhere is worse than one that lands on the page
            // carrying the same switch.
            { "UnsubscribeUrl", string.IsNullOrWhiteSpace(message.UnsubscribeUrl)
                ? message.AlertUrl
                : message.UnsubscribeUrl },
        };

        EmailTarget target = new(null, message.Uid, message.Email, template.SubjectTemplate, template.HtmlContent);
        _dbContext.EmailTargets.Add(target);
        await _dbContext.SaveChangesAsync(context.CancellationToken);

        DispatchEmailCommand dispatchCommand = new()
        {
            TargetId = target.Id,
            JobId = null,
            Email = message.Email,
            Subject = template.SubjectTemplate,
            RawTemplate = template.HtmlContent,
            TemplateData = templateData
        };

        await context.Publish(dispatchCommand, context.CancellationToken);

        activity?.SetStatus(ActivityStatusCode.Ok);
        _logger.LogInformation("Successfully dispatched Job Alert email command for {Email}", message.Email);
    }
}
