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
    /// "New jobs match your preferences." The only marketing-shaped mail this service sends:
    /// a standing subscription rather than a transactional message about the recipient's own
    /// account, so the publisher filters on commercial-message consent and every send carries
    /// an unsubscribe link.
    /// </summary>
    public string JobAlertReadyTemplateSlug { get; init; } = string.Empty;

    /// <summary>
    /// Internal notification for an enquiry submitted from a public service landing page.
    ///
    /// Unlike every other slot here, an unconfigured slug is NOT fatal: SubmitLeadEndpoint
    /// falls back to plain markup and logs Critical, because a misconfigured template must
    /// never cost a sales lead.
    /// </summary>
    public string ServiceLeadTemplateSlug { get; init; } = string.Empty;
}
