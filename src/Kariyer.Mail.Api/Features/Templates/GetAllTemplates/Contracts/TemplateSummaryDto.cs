namespace Kariyer.Mail.Api.Features.Templates.GetAllTemplates.Contracts;

public sealed record TemplateSummaryDto(Ulid Id, string Name, bool IsArchived, bool IsSystemTemplate, string? Slug, DateTime CreatedAt, DateTime? UpdatedAt);
