namespace Kariyer.Mail.Api.Features.Templates.GetAllTemplates.Contracts;

public sealed record TemplateSummaryDto(Ulid Id, string Name, bool IsArchived, DateTime CreatedAt, DateTime? UpdatedAt);