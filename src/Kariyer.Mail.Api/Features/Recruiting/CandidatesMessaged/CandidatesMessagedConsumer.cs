using System.Diagnostics;
using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Features.Templates;
using Kariyer.Messaging.Contracts.Recruiting;
using MassTransit;
using Microsoft.Extensions.Options;

namespace Kariyer.Mail.Api.Features.Recruiting.CandidatesMessaged;

/// <summary>
/// Sends a company's message to each applicant it chose in the employer portal — one mail per
/// recipient, so each greeting names its reader and no candidate sees another's address.
///
/// The recipients are final: the company picked them and the recruiting service resolved the
/// addresses. Nothing here re-filters, because nothing here knows why someone was included.
/// </summary>
internal sealed class CandidatesMessagedConsumer : IConsumer<CandidatesMessagedEvent>
{
    private readonly ILogger<CandidatesMessagedConsumer> _logger;
    private readonly EmailTemplateSettings _templateSettings;
    private readonly ITemplateResolutionService _templateService;
    private readonly MailDbContext _dbContext;

    public CandidatesMessagedConsumer(
        ILogger<CandidatesMessagedConsumer> logger,
        IOptions<EmailTemplateSettings> templateOptions,
        ITemplateResolutionService templateService,
        MailDbContext dbContext)
    {
        _logger = logger;
        _templateSettings = templateOptions.Value;
        _templateService = templateService;
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<CandidatesMessagedEvent> context)
    {
        CandidatesMessagedEvent message = context.Message;

        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("ProcessCandidatesMessagedEvent");
        activity?.SetTag("mail.event_type", "recruiting.candidates.messaged");
        activity?.SetTag("message.id", message.MessageId);
        activity?.SetTag("mail.recipients", message.Recipients.Count);

        string subject = string.IsNullOrWhiteSpace(message.Subject)
            ? $"{message.JobTitle} başvurunuz hakkında"
            : message.Subject;
        string body = AsHtml(message.Body);

        var recipients = message.Recipients
            .Where(r => !string.IsNullOrWhiteSpace(r.Email))
            .DistinctBy(r => r.Email.Trim().ToLowerInvariant())
            .Select(r => (
                (string?)r.CandidateUid,
                r.Email.Trim(),
                new Dictionary<string, string>
                {
                    { "CandidateName", r.Name },
                    { "CompanyName", message.CompanyName },
                    { "JobTitle", message.JobTitle },
                    { "Subject", subject },
                    { "Message", body },
                    { "SenderName", message.SenderName },
                }))
            .ToList();

        await RecruitingMail.QueueManyAsync(
            _dbContext, context, _templateService,
            _templateSettings.CandidateMessageTemplateSlug,
            nameof(EmailTemplateSettings.CandidateMessageTemplateSlug),
            recipients,
            context.CancellationToken);

        activity?.SetStatus(ActivityStatusCode.Ok);
        _logger.LogInformation(
            "Queued company message {MessageId} for {Count} recipient(s) on {JobTitle}",
            message.MessageId, recipients.Count, message.JobTitle);
    }

    /// <summary>
    /// The company typed plain text. It is encoded before it reaches a template — Scriban does not
    /// escape on its own, and a recruiter's "<" must not become markup in a candidate's inbox —
    /// and its line breaks are kept.
    /// </summary>
    internal static string AsHtml(string text) =>
        text.Trim()
            // Only the five characters HTML gives meaning to: WebUtility.HtmlEncode would also turn
            // Turkish letters such as "ü" into numeric entities.
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;")
            .Replace("\r\n", "\n")
            .Replace("\n", "<br>");
}
