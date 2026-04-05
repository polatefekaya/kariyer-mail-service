namespace Kariyer.Mail.Api.Common.Configuration;

public sealed class EmailTemplateSettings
{
    public const string SectionName = "EmailTemplates";

    public string AccountCreatedTemplateId { get; init; } = string.Empty;
    public string AccountCompletedTemplateId { get; init; } = string.Empty;
    public string AccountFrozenTemplateId { get; init; } = string.Empty;
    public string AccountDeletedTemplateId { get; init; } = string.Empty;
    
    public string AccountDidNotCompletedStep1TemplateId { get; init; } = string.Empty;
    public string AccountDidNotCompletedStep2TemplateId { get; init; } = string.Empty;
    public string AccountDidNotCompletedStep3TemplateId { get; init; } = string.Empty;
    
    public string AccountApprovedTemplateId { get; init; } = string.Empty;
    public string AccountRejectedTemplateId { get; init; } = string.Empty;
    
    public string AdminNotificationEmail { get; init; } = string.Empty;
    public string AdminCompanyCompletedTemplateId { get; init; } = string.Empty;
}