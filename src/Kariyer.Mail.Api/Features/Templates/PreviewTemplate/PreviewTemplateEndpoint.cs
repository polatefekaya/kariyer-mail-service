using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Web;

namespace Kariyer.Mail.Api.Features.Templates.PreviewTemplate;

/// <summary>Preview of a template as saved, rendered against its own slot's vocabulary.</summary>
internal sealed class PreviewTemplateEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("templates/{id:ulid}/preview", async (
            Ulid id,
            PreviewTemplateRequest request,
            ITemplateResolutionService templateService,
            ITemplateContextResolver contextResolver,
            ITemplatePreviewService previewService,
            CancellationToken ct) =>
        {
            EmailTemplate? template = await templateService.GetTemplateAsync(id, ct);

            if (template == null) return Results.NotFound();

            TemplatePreviewResult result = await previewService.RenderAsync(
                template.SubjectTemplate,
                template.HtmlContent,
                contextResolver.ResolveContext(template.Slug),
                request.DummyData?.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
                ct);

            return PreviewResults.ToHttpResult(result);
        })
        .WithTags("Templates");
    }
}
