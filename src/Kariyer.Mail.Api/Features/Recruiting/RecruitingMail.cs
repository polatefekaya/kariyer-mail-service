using System.Diagnostics;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Features.DispatchEmail;
using Kariyer.Mail.Api.Features.Templates;
using MassTransit;

namespace Kariyer.Mail.Api.Features.Recruiting;

/// <summary>
/// The step every başvuru mail ends with: resolve the slot's template, record the target, hand
/// it to the dispatcher. The same sequence the interview consumers spell out inline, shared here
/// because the application mails send to more than one recipient per event.
///
/// Missing configuration throws, as it does in every consumer in this service: an unset slug or
/// an unassigned slot is a deployment error, and failing loudly puts the message on the error
/// queue where it can be replayed once the admin panel has a template on the slot.
/// </summary>
internal static class RecruitingMail
{
    public const string DefaultTimeZone = "Europe/Istanbul";

    /// <summary>
    /// The same step for many recipients of one slot: the template is resolved once and every
    /// target and dispatch commits in a single SaveChanges.
    /// </summary>
    public static async Task QueueManyAsync(
        MailDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        ITemplateResolutionService templateService,
        string slug,
        string settingsKey,
        IEnumerable<(string? RecipientUserId, string Email, Dictionary<string, string> TemplateData)> recipients,
        CancellationToken ct)
    {
        Activity.Current?.SetTag("mail.template_slug", slug);

        if (string.IsNullOrWhiteSpace(slug))
        {
            Activity.Current?.SetStatus(ActivityStatusCode.Error, "Missing Template Slug Configuration");
            throw new InvalidOperationException($"CRITICAL: {settingsKey} is missing in configuration.");
        }

        EmailTemplate? template = await templateService.GetBySlugAsync(slug, ct);

        if (template == null)
        {
            DiagnosticsConfig.TemplateNotFoundCounter.Add(1, new KeyValuePair<string, object?>("slug", slug));
            Activity.Current?.SetStatus(ActivityStatusCode.Error, "Template Not Found");
            throw new InvalidOperationException($"CRITICAL: Template with slug '{slug}' not found.");
        }

        foreach ((string? recipientUserId, string email, Dictionary<string, string> templateData) in recipients)
        {
            EmailTarget target = new(null, recipientUserId, email, template.SubjectTemplate, template.HtmlContent);

            dbContext.EmailTargets.Add(target);

            await publishEndpoint.Publish(new DispatchEmailCommand
            {
                TargetId = target.Id,
                JobId = null,
                Email = email,
                Subject = template.SubjectTemplate,
                RawTemplate = template.HtmlContent,
                TemplateData = templateData
            }, ct);
        }

        await dbContext.SaveChangesAsync(ct);
    }

    public static async Task QueueAsync(
        MailDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        ITemplateResolutionService templateService,
        string slug,
        string settingsKey,
        string? recipientUserId,
        string email,
        Dictionary<string, string> templateData,
        CancellationToken ct)
    {
        Activity.Current?.SetTag("mail.template_slug", slug);

        if (string.IsNullOrWhiteSpace(slug))
        {
            Activity.Current?.SetStatus(ActivityStatusCode.Error, "Missing Template Slug Configuration");
            throw new InvalidOperationException($"CRITICAL: {settingsKey} is missing in configuration.");
        }

        EmailTemplate? template = await templateService.GetBySlugAsync(slug, ct);

        if (template == null)
        {
            DiagnosticsConfig.TemplateNotFoundCounter.Add(1, new KeyValuePair<string, object?>("slug", slug));
            Activity.Current?.SetStatus(ActivityStatusCode.Error, "Template Not Found");
            throw new InvalidOperationException($"CRITICAL: Template with slug '{slug}' not found. Cannot send to {email}.");
        }

        EmailTarget target = new(null, recipientUserId, email, template.SubjectTemplate, template.HtmlContent);

        dbContext.EmailTargets.Add(target);

        // With the bus outbox on, this publish is written in the same SaveChanges as the target —
        // a target never exists without its dispatch, nor the reverse.
        await publishEndpoint.Publish(new DispatchEmailCommand
        {
            TargetId = target.Id,
            JobId = null,
            Email = email,
            Subject = template.SubjectTemplate,
            RawTemplate = template.HtmlContent,
            TemplateData = templateData
        }, ct);

        await dbContext.SaveChangesAsync(ct);
    }
}
