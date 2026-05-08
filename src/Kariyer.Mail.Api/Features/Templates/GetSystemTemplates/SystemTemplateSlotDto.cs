using Kariyer.Mail.Api.Features.Templates.GetTemplate.Contracts;

namespace Kariyer.Mail.Api.Features.Templates.GetSystemTemplates;

public sealed record SystemTemplateSlotDto(
    string Context,
    string Description,
    string SettingsKey,
    SystemTemplateStatus Status,
    TemplateDetailDto? Template);

public enum SystemTemplateStatus
{
    Configured,  // ID in settings + template found in DB
    Empty,       // no ID configured in settings
    NotFound     // ID set in settings but template missing from DB
}
