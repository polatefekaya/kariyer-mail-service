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
}
