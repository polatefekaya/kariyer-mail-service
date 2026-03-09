using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Web;
using Microsoft.EntityFrameworkCore;
using Scriban;
using Scriban.Syntax;

namespace Kariyer.Mail.Api.Features.Templates.PreviewTemplate;

internal sealed class PreviewTemplateEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("templates/{id:ulid}/preview", async (
            Ulid id,
            PreviewTemplateRequest request,
            MailDbContext dbContext,
            CancellationToken ct) =>
        {
            var templateData = await dbContext.EmailTemplates
                .AsNoTracking()
                .Where(t => t.Id == id)
                .Select(t => new { t.SubjectTemplate, t.HtmlContent })
                .FirstOrDefaultAsync(ct);

            if (templateData == null) return Results.NotFound();

            try
            {
                Template compiledBody = Template.Parse(templateData.HtmlContent);
                Template compiledSubject = Template.Parse(templateData.SubjectTemplate);

                if (compiledBody.HasErrors || compiledSubject.HasErrors)
                {
                    return Results.BadRequest(new 
                    { 
                        Message = "Syntax error in template.", 
                        BodyErrors = compiledBody.Messages,
                        SubjectErrors = compiledSubject.Messages 
                    });
                }

                string renderedBody = await compiledBody.RenderAsync(request.DummyData);
                string renderedSubject = await compiledSubject.RenderAsync(request.DummyData);

                return Results.Ok(new 
                { 
                    RenderedSubject = renderedSubject, 
                    RenderedHtml = renderedBody 
                });
            }
            catch (ScriptRuntimeException ex)
            {
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