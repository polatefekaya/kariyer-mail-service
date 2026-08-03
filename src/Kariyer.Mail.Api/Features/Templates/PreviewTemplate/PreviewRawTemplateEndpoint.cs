using System.Diagnostics;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;

namespace Kariyer.Mail.Api.Features.Templates.PreviewTemplate;

/// <summary>Live preview for content being edited, before it is saved.</summary>
internal sealed class PreviewRawTemplateEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("templates/preview", async (
            PreviewRawTemplateRequest request,
            ITemplatePreviewService previewService,
            ILogger<PreviewRawTemplateEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("PreviewRawTemplate");
            activity?.SetTag("template.context", request.Context ?? "default");

            logger.LogDebug("Received stateless preview request. Subject Length: {SubjectLength}, Body Length: {BodyLength}",
                request.SubjectTemplate?.Length ?? 0, request.HtmlContent?.Length ?? 0);

            TemplatePreviewResult result = await previewService.RenderAsync(
                request.SubjectTemplate,
                request.HtmlContent,
                request.Context,
                request.DummyData?.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
                ct);

            if (!result.Succeeded)
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.Message);
                logger.LogWarning("Preview failed for context [{Context}]: {ErrorKind}", result.Context, result.ErrorKind);
            }
            else if (result.UnknownVariables.Count > 0)
            {
                logger.LogDebug("Preview for context [{Context}] referenced unknown variables: {Variables}",
                    result.Context, string.Join(", ", result.UnknownVariables));
            }

            return PreviewResults.ToHttpResult(result);
        })
        .WithTags("Templates");
    }
}
