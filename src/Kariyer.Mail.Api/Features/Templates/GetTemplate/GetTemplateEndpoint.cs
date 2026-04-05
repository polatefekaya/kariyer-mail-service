using System.Diagnostics;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;
using Kariyer.Mail.Api.Features.Templates.GetTemplate.Contracts;

namespace Kariyer.Mail.Api.Features.Templates.GetTemplate;

internal sealed class GetTemplateEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("templates/{id:ulid}", async (
            Ulid id,
            ITemplateResolutionService templateService,
            ILogger<GetTemplateEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("GetSingleTemplate");
            activity?.SetTag("template.id", id.ToString());

            EmailTemplate? template = await templateService.GetTemplateAsync(id, ct);

            if (template == null) 
            {
                logger.LogWarning("Fetch failed: Template [{TemplateId}] not found.", id);
                return Results.NotFound();
            }

            TemplateDetailDto dto = new (
                template.Id, 
                template.Name, 
                template.SubjectTemplate, 
                template.HtmlContent, 
                template.IsArchived, 
                template.CreatedAt);

            return Results.Ok(dto);
        })
        .WithTags("Templates");
    }
}