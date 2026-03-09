namespace Kariyer.Mail.Api.Features.TransactionalEmail.Contracts;

public sealed record SendSingleEmailRequest(
    string? UserId,
    string Email,
    Ulid? TemplateId,
    string? Subject,
    string? BodyTemplate,
    Dictionary<string, string>? TemplateData
);