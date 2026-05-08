using System.Diagnostics;
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
            ILogger<UnmarkSystemTemplateEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("UnmarkSystemTemplate");
            activity?.SetTag("template.id", id.ToString());

            int updatedCount = await dbContext.EmailTemplates
                .Where(t => t.Id == id && t.IsSystemTemplate)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.IsSystemTemplate, false)
                    .SetProperty(t => t.UpdatedAt, DateTime.UtcNow),
                ct);

            if (updatedCount == 0)
            {
                bool exists = await dbContext.EmailTemplates.AnyAsync(t => t.Id == id, ct);
                if (!exists)
                {
                    logger.LogWarning("Unmark-system failed: Template [{TemplateId}] not found.", id);
                    return Results.NotFound(new { Message = "Template not found." });
                }

                // Already not a system template — idempotent success
                logger.LogDebug("Template [{TemplateId}] was already not a system template.", id);
                return Results.NoContent();
            }

            IDatabase garnet = multiplexer.GetDatabase();
            await garnet.KeyDeleteAsync("templates:all:archived_false");
            await garnet.KeyDeleteAsync("templates:all:archived_true");
            await garnet.KeyDeleteAsync($"template:detail:{id}");

            logger.LogWarning("Template [{TemplateId}] unmarked as system template. It is now unprotected.", id);
            return Results.NoContent();
        })
        .WithTags("Templates");
    }
}
