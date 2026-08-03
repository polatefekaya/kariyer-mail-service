using System.Diagnostics;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;
using Kariyer.Mail.Api.Common.Web.Filters;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Templates.UpdateTemplate;

internal sealed class UpdateTemplateEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("templates/{id:ulid}", async (
            Ulid id,
            UpdateTemplateRequest request,
            MailDbContext dbContext,
            IConnectionMultiplexer multiplexer,
            ITemplateResolutionService templateService,
            ITemplateContextResolver contextResolver,
            ILogger<UpdateTemplateEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("UpdateTemplate");
            activity?.SetTag("template.id", id.ToString());

            IDatabase garnet = multiplexer.GetDatabase();
            
            EmailTemplate? template = await dbContext.EmailTemplates
                .FirstOrDefaultAsync(t => t.Id == id, ct);

            if (template == null) 
            {
                logger.LogWarning("Update failed: Template [{TemplateId}] not found.", id);
                return Results.NotFound();
            }
            
            if (template.IsArchived)
            {
                logger.LogWarning("Update rejected: Attempted to mutate archived Template [{TemplateId}].", id);
                return Results.BadRequest(new { Message = "Cannot update an archived template. Unarchive it first." });
            }

            bool isLockedByPastJobs = await dbContext.EmailJobs.AnyAsync(j => j.TemplateId == id, ct);

            if (isLockedByPastJobs)
            {
                logger.LogWarning("Update rejected: Template [{TemplateId}] is locked because it is referenced by a historical Email Job.", id);
                return Results.Conflict(new 
                { 
                    Message = "This template has already been used in an active or historical email job. " +
                              "Its content is locked to preserve the audit trail. Please duplicate/clone this template to make changes." 
                });
            }

            string? slug = template.Slug;

            // The context is whatever slot this template is bound to — that is what decides which
            // variables are legal in it, which is exactly what the old shared editor got wrong.
            string context = contextResolver.ResolveContext(slug);
            activity?.SetTag("template.context", context);

            if (!TemplateContentValidator.TryPrepare(
                    request.SubjectTemplate, request.HtmlContent, context, contextResolver,
                    out PreparedTemplateContent prepared, out IResult? error))
            {
                logger.LogWarning("Update rejected: Template [{TemplateId}] content has Scriban syntax errors.", id);
                return error!;
            }

            template.Update(request.Name, prepared.Subject, prepared.Html);

            await dbContext.SaveChangesAsync(ct);

            await TemplateCacheKeys.InvalidateListsAsync(garnet);
            await templateService.InvalidateAsync(id, slug);

            logger.LogInformation("Template [{TemplateId}] successfully updated and caches invalidated.", id);

            // 200 rather than 204 so unknown-variable warnings can ride along on a successful save.
            return Results.Ok(new { Context = context, Warnings = prepared.Warnings });
        })
        .AddEndpointFilter<ValidationFilter<UpdateTemplateRequest>>()
        .WithTags("Templates");
    }
}