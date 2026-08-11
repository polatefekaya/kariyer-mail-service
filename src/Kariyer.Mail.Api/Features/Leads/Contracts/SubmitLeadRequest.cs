namespace Kariyer.Mail.Api.Features.Leads.Contracts;

/// <summary>
/// A service-page enquiry, as posted by the public website.
///
/// The shape is the security boundary. Unlike <c>transactional/send</c> — which takes an
/// arbitrary recipient, subject and HTML body, and is therefore internal-only — nothing here
/// lets the caller choose WHO receives the mail or WHAT it says. The recipients come from the
/// admin-managed notification list and the wording comes from a stored template, both resolved
/// server-side. That is what makes it safe to expose this endpoint to the open internet.
///
/// Do not add a subject, body, recipient or template field to this record. If a caller ever
/// needs one, it belongs on the internal endpoint instead.
/// </summary>
public sealed record SubmitLeadRequest(
    string FullName,
    string CompanyName,
    string Email,
    string Phone,
    string? Message,
    /// <summary>Which service page produced this, e.g. <c>/fuar-etkinlik-personeli-temini</c>.</summary>
    string PagePath,
    /// <summary>Human-readable name of that page, for the subject line and the notification body.</summary>
    string PageLabel,
    /// <summary>UI language at submit time (<c>tr</c>/<c>en</c>), so sales knows which to reply in.</summary>
    string? Locale,
    /// <summary>
    /// Honeypot. A real form leaves this empty because the field is hidden from humans; most
    /// naive bots fill every input they find. Submissions that carry a value are accepted with
    /// a 202 and silently dropped — telling a bot it failed just teaches it to try again.
    /// </summary>
    string? Website
);
