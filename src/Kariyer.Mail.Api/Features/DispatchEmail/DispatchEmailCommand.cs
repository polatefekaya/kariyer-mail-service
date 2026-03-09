namespace Kariyer.Mail.Api.Features.DispatchEmail;

public sealed record DispatchEmailCommand
{
    public Ulid? JobId { get; init; }
    public Ulid TargetId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string RawTemplate { get; init; } = string.Empty;
    public Dictionary<string, string> TemplateData { get; init; } = new();
}