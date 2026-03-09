namespace Kariyer.Mail.Api.Features.Templates.CreateTemplate;

public sealed record CreateTemplateRequest(string Name, string SubjectTemplate, string HtmlContent);