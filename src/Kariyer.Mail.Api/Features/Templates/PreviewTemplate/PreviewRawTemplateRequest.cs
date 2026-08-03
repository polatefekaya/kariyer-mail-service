namespace Kariyer.Mail.Api.Features.Templates.PreviewTemplate;

/// <summary>
/// <paramref name="Context"/> selects which vocabulary the server seeds the preview with. Omitting
/// it falls back to <c>BulkEmail</c>, which keeps older clients working. <paramref name="DummyData"/>
/// is layered on top of the seed, so callers only need to send values they want to override.
/// </summary>
public sealed record PreviewRawTemplateRequest(
    string? SubjectTemplate,
    string? HtmlContent,
    Dictionary<string, object>? DummyData,
    string? Context = null);
