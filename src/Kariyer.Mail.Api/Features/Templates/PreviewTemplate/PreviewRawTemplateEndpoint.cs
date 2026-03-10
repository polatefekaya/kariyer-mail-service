using System.Diagnostics;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;
using Scriban;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Kariyer.Mail.Api.Features.Templates.PreviewTemplate;

internal sealed class PreviewRawTemplateEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("templates/preview", async (
            PreviewRawTemplateRequest request,
            ILogger<PreviewRawTemplateEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("PreviewRawTemplate");
            
            logger.LogDebug("Received stateless preview request. Subject Length: {SubjectLength}, Body Length: {BodyLength}", 
                request.SubjectTemplate?.Length ?? 0, request.HtmlContent?.Length ?? 0);

            if (string.IsNullOrWhiteSpace(request.HtmlContent) && string.IsNullOrWhiteSpace(request.SubjectTemplate))
            {
                return Results.BadRequest(new { Message = "Cannot preview empty content." });
            }

            try
            {
                Template compiledBody = Template.Parse(request.HtmlContent ?? string.Empty);
                Template compiledSubject = Template.Parse(request.SubjectTemplate ?? string.Empty);

                if (compiledBody.HasErrors || compiledSubject.HasErrors)
                {
                    logger.LogWarning("Stateless Scriban compilation failed due to syntax errors.");
                    return Results.BadRequest(new 
                    { 
                        Message = "Syntax error in template.", 
                        BodyErrors = compiledBody.Messages,
                        SubjectErrors = compiledSubject.Messages 
                    });
                }

                ScriptObject scriptObject = new ();
                scriptObject.Import(request.DummyData);

                TemplateContext context = new() 
                {
                    MemberRenamer = member => member.Name,
                    MemberFilter = null, 
                    StrictVariables = true
                };
                
                context.PushGlobal(scriptObject);

                string renderedBody = await compiledBody.RenderAsync(context);
                string renderedSubject = await compiledSubject.RenderAsync(context);

                logger.LogInformation("Successfully rendered stateless template preview.");

                return Results.Ok(new 
                { 
                    RenderedSubject = renderedSubject, 
                    RenderedHtml = renderedBody 
                });
            }
            catch (ScriptRuntimeException ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                logger.LogError(ex, "A runtime error occurred during stateless Scriban rendering.");
                
                return Results.BadRequest(new 
                { 
                    Message = "A runtime error occurred while rendering the template.", 
                    Details = ex.Message 
                });
            }
        })
        .WithTags("Templates");
    }
}