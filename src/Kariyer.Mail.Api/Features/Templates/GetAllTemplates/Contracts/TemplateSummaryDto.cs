namespace Kariyer.Mail.Api.Features.Templates.GetAllTemplates.Contracts;

/// <summary><c>Context</c> is derived from <c>Slug</c> at read time — see TemplateDetailDto.</summary>
public sealed record TemplateSummaryDto(
    Ulid Id,
    string Name,
    bool IsArchived,
    bool IsSystemTemplate,
    string? Slug,
    string Context,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
