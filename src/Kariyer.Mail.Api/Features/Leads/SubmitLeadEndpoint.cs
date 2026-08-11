using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;
using Kariyer.Mail.Api.Common.Web.Filters;
using Kariyer.Mail.Api.Features.AdminNotifications;
using Kariyer.Mail.Api.Features.DispatchEmail;
using Kariyer.Mail.Api.Features.Leads.Contracts;
using Kariyer.Mail.Api.Features.Templates;
using MassTransit;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Leads;

/// <summary>
/// The ONLY endpoint in this service intended to be reachable from the public internet.
///
/// Everything else here — transactional/send, templates/*, bulk/*, schedules/* — is internal.
/// `transactional/send` in particular accepts an arbitrary recipient, subject and HTML body
/// with no authentication, so exposing it publicly would be an open mail relay. This endpoint
/// exists precisely so the website never needs it: the caller supplies only facts about the
/// enquiry, and WHO is mailed and WHAT is said are both resolved server-side.
///
/// If you route this service at the gateway, route THIS PATH ONLY.
/// </summary>
internal sealed class SubmitLeadEndpoint : IEndpoint
{
    /// <summary>Named rate-limit policy, configured in Program.cs.</summary>
    public const string RateLimitPolicy = "lead-submit";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("leads", async (
            HttpContext http,
            SubmitLeadRequest request,
            IAdminNotificationService notificationService,
            ITemplateResolutionService templateService,
            MailDbContext dbContext,
            IPublishEndpoint publishEndpoint,
            IConnectionMultiplexer multiplexer,
            IOptions<EmailTemplateSettings> templateSettings,
            ILogger<SubmitLeadEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("SubmitServiceLead");
            activity?.SetTag("lead.page_path", request.PagePath);

            // Honeypot. Accepted, logged, dropped. Answering 400 would tell a bot which field
            // caught it; a 202 that goes nowhere teaches it nothing and costs us nothing.
            if (!string.IsNullOrWhiteSpace(request.Website))
            {
                logger.LogInformation("Dropped honeypot lead submission from page {PagePath}.", request.PagePath);
                activity?.SetTag("lead.outcome", "honeypot");
                return Results.Accepted();
            }

            // A double-clicked submit button is one lead, not two. Keyed on the payload rather
            // than on a client-supplied header — unlike transactional/send, whose callers are
            // our own services and can be trusted to mint an idempotency key, this caller is a
            // browser and cannot.
            string fingerprint = Fingerprint(request);
            IDatabase garnet = multiplexer.GetDatabase();
            bool isFirst = await garnet.StringSetAsync(
                $"idempotency:lead:{fingerprint}", "locked", TimeSpan.FromMinutes(10), When.NotExists);

            if (!isFirst)
            {
                logger.LogInformation("Suppressed duplicate lead submission for {Email}.", request.Email);
                activity?.SetTag("lead.outcome", "duplicate");
                return Results.Accepted();
            }

            IReadOnlyList<string> recipients = await notificationService.GetActiveEmailsAsync(ct);

            if (recipients.Count == 0)
            {
                // The visitor did nothing wrong, so they still get a 202 — but this is a lost
                // sales lead, so log it at Error WITH the contact details. Recovering it from
                // the log is ugly; losing it silently is worse.
                logger.LogError(
                    "LEAD LOST — no active notification recipients configured. " +
                    "Page [{PagePath}] Name [{FullName}] Company [{CompanyName}] Email [{Email}] Phone [{Phone}]",
                    request.PagePath, request.FullName, request.CompanyName, request.Email, request.Phone);
                activity?.SetTag("lead.outcome", "no_recipients");
                return Results.Accepted();
            }

            Dictionary<string, string> templateData = new()
            {
                { "FullName", request.FullName },
                { "CompanyName", request.CompanyName },
                { "Email", request.Email },
                { "Phone", request.Phone },
                { "Message", string.IsNullOrWhiteSpace(request.Message) ? "Belirtilmedi" : request.Message },
                { "PageLabel", request.PageLabel },
                { "PagePath", request.PagePath },
                { "Locale", string.IsNullOrWhiteSpace(request.Locale) ? "tr" : request.Locale },
                { "SubmittedAt", DateTimeOffset.UtcNow.ToString("g") },
            };

            // The stored template is an enhancement, not a dependency. The sibling consumers
            // throw when their slug is missing, which is right for a notification nobody is
            // waiting on — but throwing here would 500 a paying visitor's enquiry and bin it.
            // A misconfigured template must never cost a sales lead, so fall back to plain
            // markup and shout about it instead.
            string slug = templateSettings.Value.ServiceLeadTemplateSlug;
            EmailTemplate? template = string.IsNullOrWhiteSpace(slug)
                ? null
                : await templateService.GetBySlugAsync(slug, ct);

            if (template is null)
            {
                DiagnosticsConfig.TemplateNotFoundCounter.Add(1,
                    new KeyValuePair<string, object?>("slug", slug ?? "unset"));
                logger.LogCritical(
                    "Service-lead template [{Slug}] is missing. Falling back to plain markup — fix the EmailTemplates configuration.",
                    string.IsNullOrWhiteSpace(slug) ? "<unset>" : slug);
            }

            string subject = template?.SubjectTemplate ?? $"Yeni hizmet talebi — {request.PageLabel}";
            string body = template?.HtmlContent ?? FallbackBody();

            // Add every target and publish every command FIRST, then save EXACTLY ONCE.
            //
            // The order matters and is not stylistic. MassTransit runs an EF transactional
            // outbox here (`AddEntityFrameworkOutbox` + `UseBusOutbox`, MessagingExtensions),
            // so `Publish` never reaches the broker directly — it queues an outbox row that is
            // dispatched by the NEXT `SaveChangesAsync`. Saving inside the loop therefore
            // flushed each recipient's message on the FOLLOWING iteration and left the last one
            // queued forever: N recipients produced N-1 emails, and a single recipient produced
            // none at all.
            //
            // `EmailTarget.Id` is a Ulid assigned in the constructor, so a command can safely
            // reference it before the row is written — which is exactly why
            // SendSingleEmailEndpoint also publishes before it saves.
            //
            // One save is also the only atomic version: every target row and every queued
            // message commits together, or nothing does.
            foreach (string recipient in recipients)
            {
                EmailTarget target = new(null, null, recipient, subject, body);
                dbContext.EmailTargets.Add(target);

                DispatchEmailCommand command = new()
                {
                    JobId = null,
                    TargetId = target.Id,
                    Email = recipient,
                    Subject = subject,
                    RawTemplate = body,
                    TemplateData = templateData,
                };

                await publishEndpoint.Publish(command, ct);
            }

            await dbContext.SaveChangesAsync(ct);

            activity?.SetTag("lead.recipient_count", recipients.Count);
            activity?.SetTag("lead.outcome", "dispatched");
            activity?.SetStatus(ActivityStatusCode.Ok);

            logger.LogInformation(
                "Dispatched service lead from [{PagePath}] to {Count} recipients.",
                request.PagePath, recipients.Count);

            return Results.Accepted();
        })
        .AddEndpointFilter<ValidationFilter<SubmitLeadRequest>>()
        .RequireRateLimiting(RateLimitPolicy)
        .AllowAnonymous()
        .WithTags("Leads")
        .WithSummary("Submit a service-page enquiry. Public.");
    }

    /// <summary>
    /// Identifies a repeated submission of the same enquiry. Hashed rather than stored raw
    /// because the key lands in Garnet, and an email address is personal data.
    /// </summary>
    private static string Fingerprint(SubmitLeadRequest r)
    {
        string material = string.Join(
            '|',
            r.Email.Trim().ToLowerInvariant(),
            r.Phone.Trim(),
            r.PagePath.Trim(),
            (r.Message ?? string.Empty).Trim());

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..32];
    }

    /// <summary>
    /// Used only when the configured template is missing. Scriban placeholders, so the normal
    /// dispatch pipeline renders it with the same templateData a real template would receive.
    /// </summary>
    private static string FallbackBody() =>
        """
        <h2>Yeni hizmet talebi</h2>
        <p><strong>Sayfa:</strong> {{ PageLabel }} ({{ PagePath }})</p>
        <table cellpadding="6" style="border-collapse:collapse">
          <tr><td><strong>Ad Soyad</strong></td><td>{{ FullName }}</td></tr>
          <tr><td><strong>Şirket</strong></td><td>{{ CompanyName }}</td></tr>
          <tr><td><strong>E-Posta</strong></td><td>{{ Email }}</td></tr>
          <tr><td><strong>Telefon</strong></td><td>{{ Phone }}</td></tr>
          <tr><td><strong>Dil</strong></td><td>{{ Locale }}</td></tr>
          <tr><td><strong>Tarih</strong></td><td>{{ SubmittedAt }}</td></tr>
        </table>
        <p><strong>Mesaj</strong><br/>{{ Message }}</p>
        """;
}

/// <summary>
/// Partition key for the rate limiter. Behind the gateway every request arrives from the same
/// socket, so <c>RemoteIpAddress</c> alone would rate-limit the whole internet as one bucket.
/// The forwarded header is trusted for exactly this reason — the service is not meant to be
/// reachable except through the proxy, and the consequence of a spoofed value is a slightly
/// more generous limit, not access to anything.
/// </summary>
internal static class LeadRateLimitPartition
{
    public static string Resolve(HttpContext http)
    {
        string? forwarded = http.Request.Headers["X-Forwarded-For"].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            // Left-most entry is the original client; the rest are proxies.
            string first = forwarded.Split(',')[0].Trim();
            if (!string.IsNullOrWhiteSpace(first)) return first;
        }

        IPAddress? remote = http.Connection.RemoteIpAddress;
        return remote?.ToString() ?? "unknown";
    }
}
