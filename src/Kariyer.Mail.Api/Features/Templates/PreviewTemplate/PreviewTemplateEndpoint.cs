using System.Text.Json;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Web;
using Microsoft.EntityFrameworkCore;
using Scriban;
using Scriban.Runtime;
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

                ScriptObject scriptObject = new();
                if (request.DummyData != null)
                    foreach (var (key, val) in request.DummyData)
                        scriptObject[key] = UnwrapJsonElement(val);

                TemplateContext ctx = new()
                {
                    MemberRenamer = member => member.Name,
                    StrictVariables = false
                };
                ctx.PushGlobal(scriptObject);

                string renderedBody = await compiledBody.RenderAsync(ctx);
                string renderedSubject = await compiledSubject.RenderAsync(ctx);

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

    private static object? UnwrapJsonElement(object? value) => value switch
    {
        JsonElement { ValueKind: JsonValueKind.String } el => el.GetString(),
        JsonElement { ValueKind: JsonValueKind.True   }    => true,
        JsonElement { ValueKind: JsonValueKind.False  }    => false,
        JsonElement { ValueKind: JsonValueKind.Null   }    => null,
        JsonElement { ValueKind: JsonValueKind.Number } el =>
            el.TryGetInt64(out long l) ? (object)l : el.GetDouble(),
        _ => value
    };
}