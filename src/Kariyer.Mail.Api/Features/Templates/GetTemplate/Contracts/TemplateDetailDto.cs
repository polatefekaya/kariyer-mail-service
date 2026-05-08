namespace Kariyer.Mail.Api.Features.Templates.GetTemplate.Contracts;

public sealed record TemplateDetailDto(Ulid Id, string Name, string SubjectTemplate, string HtmlContent, bool IsArchived, bool IsSystemTemplate, string? Slug, DateTime CreatedAt, DateTime? UpdatedAt);
