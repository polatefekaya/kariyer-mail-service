using System.Diagnostics;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Templates.DeleteTemplate;

internal sealed class DeleteTemplateEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("templates/{id:ulid}", async (
            Ulid id,
            MailDbContext dbContext,
            IConnectionMultiplexer multiplexer,
            ILogger<DeleteTemplateEndpoint> logger,
            ITemplateResolutionService templateService,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("DeleteTemplate");
            activity?.SetTag("template.id", id.ToString());

            IDatabase garnet = multiplexer.GetDatabase();
            
            bool isReferenced = await dbContext.EmailJobs.AnyAsync(j => j.TemplateId == id, ct);
            
            if (isReferenced)
            {
                logger.LogWarning("Hard delete rejected: Template [{TemplateId}] is referenced by existing Email Jobs. Recommend soft-archive.", id);
                return Results.Conflict(new 
                { 
                    Message = "This template has been used in one or more bulk email jobs. It cannot be deleted to preserve the historical audit trail. Consider archiving it instead." 
                });
            }

            int deletedCount = await dbContext.EmailTemplates
                .Where(t => t.Id == id)
                .ExecuteDeleteAsync(ct);
                
            if (deletedCount == 0)
            {
                logger.LogWarning("Hard delete failed: Template [{TemplateId}] not found.", id);
                return Results.NotFound(new { Message = "Template not found." });
            }

            await garnet.KeyDeleteAsync("templates:all:archived_false");
            await garnet.KeyDeleteAsync("templates:all:archived_true");
            await templateService.InvalidateTemplateCacheAsync(id);

            logger.LogInformation("Template [{TemplateId}] successfully hard-deleted.", id);

            return Results.NoContent();
        })
        .WithTags("Templates");
    }
}