using System.Diagnostics;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Templates.MarkSystemTemplate;

internal sealed class MarkSystemTemplateEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("templates/{id:ulid}/mark-system", async (
            Ulid id,
            MailDbContext dbContext,
            IConnectionMultiplexer multiplexer,
            ILogger<MarkSystemTemplateEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("MarkSystemTemplate");
            activity?.SetTag("template.id", id.ToString());

            var guards = await dbContext.EmailTemplates
                .Where(t => t.Id == id)
                .Select(t => new { t.IsSystemTemplate, t.IsArchived })
                .FirstOrDefaultAsync(ct);

            if (guards == null)
            {
                logger.LogWarning("Mark-system failed: Template [{TemplateId}] not found.", id);
                return Results.NotFound(new { Message = "Template not found." });
            }

            if (guards.IsArchived)
            {
                logger.LogWarning("Mark-system rejected: Template [{TemplateId}] is archived.", id);
                return Results.BadRequest(new { Message = "Archived templates cannot be marked as system templates." });
            }

            if (guards.IsSystemTemplate)
            {
                logger.LogDebug("Template [{TemplateId}] was already marked as system template.", id);
                return Results.NoContent();
            }

            int updatedCount = await dbContext.EmailTemplates
                .Where(t => t.Id == id && !t.IsSystemTemplate && !t.IsArchived)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.IsSystemTemplate, true)
                    .SetProperty(t => t.UpdatedAt, DateTime.UtcNow),
                ct);

            if (updatedCount == 0)
            {
                logger.LogWarning("Mark-system failed: Template [{TemplateId}] state changed concurrently.", id);
                return Results.Conflict(new { Message = "Template state changed concurrently. Please retry." });
            }

            IDatabase garnet = multiplexer.GetDatabase();
            await TemplateCacheKeys.InvalidateListsAsync(garnet);
            await garnet.KeyDeleteAsync($"template:detail:{id}");

            logger.LogInformation("Template [{TemplateId}] marked as system template.", id);
            return Results.NoContent();
        })
        .WithTags("Templates");
    }
}
