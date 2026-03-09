namespace Kariyer.Mail.Api.Features.Templates.PreviewTemplate;

public sealed record PreviewRawTemplateRequest(string SubjectTemplate, string HtmlContent, Dictionary<string, object>? DummyData);