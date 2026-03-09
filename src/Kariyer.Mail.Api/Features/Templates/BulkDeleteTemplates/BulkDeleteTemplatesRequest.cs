namespace Kariyer.Mail.Api.Features.Templates.BulkDeleteTemplates;

public sealed record BulkDeleteTemplatesRequest(
    Ulid[] TemplateIds
);