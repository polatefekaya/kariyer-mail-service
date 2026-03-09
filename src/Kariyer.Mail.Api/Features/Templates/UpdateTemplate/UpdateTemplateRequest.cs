namespace Kariyer.Mail.Api.Features.Templates.UpdateTemplate;

public sealed record UpdateTemplateRequest(
    string Name, 
    string SubjectTemplate, 
    string HtmlContent
);