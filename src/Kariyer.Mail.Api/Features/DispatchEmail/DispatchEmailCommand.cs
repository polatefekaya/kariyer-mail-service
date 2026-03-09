namespace Kariyer.Mail.Api.Features.DispatchEmail;

public sealed record DispatchEmailCommand(
    Ulid? JobId,
    Ulid TargetId,
    string Email,
    string Subject,
    string RawTemplate,
    Dictionary<string, string> TemplateData
);