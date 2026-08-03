namespace Kariyer.Mail.Api.Features.Templates.GetTemplate.Contracts;

/// <summary>
/// <paramref name="Context"/> is derived from <paramref name="Slug"/> at read time — it is not
/// stored. A template bound to a system slot reports that slot's context; everything else reports
/// <c>BulkEmail</c>. The admin editor uses it to pick the right variable vocabulary.
/// </summary>
public sealed record TemplateDetailDto(
    Ulid Id,
    string Name,
    string SubjectTemplate,
    string HtmlContent,
    bool IsArchived,
    bool IsSystemTemplate,
    string? Slug,
    string Context,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
