namespace Kariyer.Mail.Api.Features.Templates.CreateTemplate;

/// <summary>
/// <paramref name="Context"/> is optional and never stored — it only picks which placeholder
/// vocabulary the unknown-variable warnings are checked against. Defaults to <c>BulkEmail</c>.
/// </summary>
public sealed record CreateTemplateRequest(
    string Name,
    string SubjectTemplate,
    string HtmlContent,
    string? Context = null);
