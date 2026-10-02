namespace Kariyer.Mail.Api.Common.Configuration;

public sealed class EmailTemplateSettings
{
    public const string SectionName = "EmailTemplates";

    public string AccountCreatedTemplateSlug { get; init; } = string.Empty;
    public string AccountCompletedTemplateSlug { get; init; } = string.Empty;
    public string AccountFrozenTemplateSlug { get; init; } = string.Empty;
    public string AccountDeletedTemplateSlug { get; init; } = string.Empty;

    public string AccountDidNotCompletedStep1TemplateSlug { get; init; } = string.Empty;
    public string AccountDidNotCompletedStep2TemplateSlug { get; init; } = string.Empty;
    public string AccountDidNotCompletedStep3TemplateSlug { get; init; } = string.Empty;

    public string AccountApprovedTemplateSlug { get; init; } = string.Empty;
    public string AccountRejectedTemplateSlug { get; init; } = string.Empty;

    public string AdminCompanyCompletedTemplateSlug { get; init; } = string.Empty;

    public string AccountDeletionCancelledTemplateSlug { get; init; } = string.Empty;

    public string AccountEmailChangedTemplateSlug { get; init; } = string.Empty;
    public string AccountPhoneChangedTemplateSlug { get; init; } = string.Empty;
    public string AccountUsernameChangedTemplateSlug { get; init; } = string.Empty;

    /// <summary>
    /// "New jobs match your preferences", one slot per send window.
    ///
    /// The publisher cuts a digest inside one of three configurable windows — morning, noon
    /// or evening — and names it on the event. Three slots rather than one so the copy can
    /// suit the hour it lands in; a single template would have to read equally well at 09:00
    /// and 19:00.
    ///
    /// These are the only marketing-shaped mail this service sends: a standing subscription
    /// rather than a transactional message about the recipient's own account. The publisher
    /// filters on commercial-message consent, and every send carries an unsubscribe link.
    ///
    /// Morning doubles as the fallback for an unrecognised or absent slot — see
    /// JobAlertReadyConsumer. Configure it even if you only intend to use one window.
    /// </summary>
    public string JobAlertMorningTemplateSlug { get; init; } = string.Empty;
    public string JobAlertNoonTemplateSlug { get; init; } = string.Empty;
    public string JobAlertEveningTemplateSlug { get; init; } = string.Empty;

    /// <summary>
    /// The interview mail kariyer-recruiting-service triggers: the invitation, a change to one
    /// already sent, and a cancellation.
    ///
    /// The invitation and the change carry AcceptUrl / DeclineUrl — signed, one-click links the
    /// recruiting service mints because it owns confirmation_status. They answer for whoever
    /// opens them, so a template must put them in front of the candidate and nobody else.
    ///
    /// An invitation also moves the application to the INTERVIEW stage, which publishes its own
    /// event. Nothing in this service mails on that: the invitation below is the one message.
    /// </summary>
    public string InterviewInvitedTemplateSlug { get; init; } = string.Empty;
    public string InterviewRescheduledTemplateSlug { get; init; } = string.Empty;
    public string InterviewCancelledTemplateSlug { get; init; } = string.Empty;

    /// <summary>
    /// A candidate's answer to an invitation, told to the company side (whoever invited plus
    /// the interview's participants). Two slots rather than one so "kabul etti" and "reddetti"
    /// can read differently — the second usually asks the recruiter to propose a new time.
    /// </summary>
    public string InterviewAcceptedTemplateSlug { get; init; } = string.Empty;
    public string InterviewDeclinedTemplateSlug { get; init; } = string.Empty;

    /// <summary>
    /// A new application: the candidate's acknowledgement and the company's notification, both
    /// from the one event kariyer_zamani_backend publishes when the application is committed.
    /// </summary>
    public string ApplicationSubmittedTemplateSlug { get; init; } = string.Empty;
    public string ApplicationSubmittedCompanyTemplateSlug { get; init; } = string.Empty;

    /// <summary>The company is told a candidate withdrew; the candidate already saw it on screen.</summary>
    public string ApplicationWithdrawnTemplateSlug { get; init; } = string.Empty;

    /// <summary>
    /// The decisions a candidate hears about from the pipeline: an offer, a hire, a rejection.
    ///
    /// These are HELD, not sent on receipt — see PendingStageMail. A recruiter who clicks the
    /// wrong row and corrects it within the hold never reaches the candidate. Every other stage
    /// is silent here: INTERVIEW is announced by the invitation, and a correction out of HIRED or
    /// REJECTED sends nothing.
    /// </summary>
    public string ApplicationOfferTemplateSlug { get; init; } = string.Empty;
    public string ApplicationHiredTemplateSlug { get; init; } = string.Empty;
    public string ApplicationRejectedTemplateSlug { get; init; } = string.Empty;

    /// <summary>
    /// A company's own message to a group of its applicants ("Adaylarla iletişime geç" in the
    /// employer portal). The template is the frame; the company's text arrives as Message.
    /// </summary>
    public string CandidateMessageTemplateSlug { get; init; } = string.Empty;

    /// <summary>
    /// Internal notification for an enquiry submitted from a public service landing page.
    ///
    /// Unlike every other slot here, an unconfigured slug is NOT fatal: SubmitLeadEndpoint
    /// falls back to plain markup and logs Critical, because a misconfigured template must
    /// never cost a sales lead.
    /// </summary>
    public string ServiceLeadTemplateSlug { get; init; } = string.Empty;
}
