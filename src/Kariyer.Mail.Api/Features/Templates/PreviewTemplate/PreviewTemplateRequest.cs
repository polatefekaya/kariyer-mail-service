namespace Kariyer.Mail.Api.Features.Templates.PreviewTemplate;

/// <summary>
/// Preview of a saved template. The context is derived from the template's slug, so only value
/// overrides are accepted here.
/// </summary>
public sealed record PreviewTemplateRequest(
    Dictionary<string, object>? DummyData
);
