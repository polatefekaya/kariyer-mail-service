using System.Diagnostics;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Templates.UnmarkSystemTemplate;

internal sealed class UnmarkSystemTemplateEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("templates/{id:ulid}/unmark-system", async (
            Ulid id,
            MailDbContext dbContext,
            IConnectionMultiplexer multiplexer,
            ITemplateResolutionService templateService,
            ILogger<UnmarkSystemTemplateEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("UnmarkSystemTemplate");
            activity?.SetTag("template.id", id.ToString());

            EmailTemplate? template = await dbContext.EmailTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);

            if (template == null)
            {
                logger.LogWarning("Unmark-system failed: Template [{TemplateId}] not found.", id);
                return Results.NotFound(new { Message = "Template not found." });
            }

            if (!template.IsSystemTemplate)
            {
                // Already not a system template — idempotent success
                logger.LogDebug("Template [{TemplateId}] was already not a system template.", id);
                return Results.NoContent();
            }

            string? oldSlug = template.Slug;
            template.UnmarkAsSystemTemplate(); // also clears Slug

            await dbContext.SaveChangesAsync(ct);

            IDatabase garnet = multiplexer.GetDatabase();
            await garnet.KeyDeleteAsync("templates:all:archived_false");
            await garnet.KeyDeleteAsync("templates:all:archived_true");
            await templateService.InvalidateAsync(id, oldSlug);

            logger.LogWarning("Template [{TemplateId}] (slug: {Slug}) unmarked as system template. Slug cleared. It is now unprotected.", id, oldSlug);
            return Results.NoContent();
        })
        .WithTags("Templates");
    }
}
