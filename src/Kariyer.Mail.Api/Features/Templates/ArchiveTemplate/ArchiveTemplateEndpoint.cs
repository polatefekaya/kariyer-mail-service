using System.Diagnostics;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Templates.ArchiveTemplate;

internal sealed class ArchiveTemplateEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("templates/{id:ulid}/archive", async (
            Ulid id,
            MailDbContext dbContext,
            IConnectionMultiplexer multiplexer,
            ILogger<ArchiveTemplateEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("ArchiveTemplate");
            activity?.SetTag("template.id", id.ToString());

            IDatabase garnet = multiplexer.GetDatabase();

            var guards = await dbContext.EmailTemplates
                .AsNoTracking()
                .Where(t => t.Id == id)
                .Select(t => new { t.IsSystemTemplate, t.IsArchived, t.Slug })
                .FirstOrDefaultAsync(ct);

            if (guards == null)
            {
                logger.LogWarning("Archive failed: Template [{TemplateId}] not found.", id);
                return Results.NotFound(new { Message = "Template not found or already archived." });
            }

            if (guards.IsSystemTemplate)
            {
                logger.LogWarning("Archive rejected: Template [{TemplateId}] is a system template.", id);
                return Results.Json(
                    new { Message = "System templates cannot be archived. Unmark it as a system template first if you intend to replace it." },
                    statusCode: StatusCodes.Status423Locked);
            }

            if (guards.IsArchived)
            {
                return Results.NotFound(new { Message = "Template not found or already archived." });
            }

            int updatedCount = await dbContext.EmailTemplates
                .Where(t => t.Id == id && !t.IsArchived)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.IsArchived, true)
                    .SetProperty(t => t.UpdatedAt, DateTime.UtcNow),
                ct);

            if (updatedCount == 0)
            {
                logger.LogWarning("Archive failed: Template [{TemplateId}] not found or already archived.", id);
                return Results.NotFound(new { Message = "Template not found or already archived." });
            }

            await garnet.KeyDeleteAsync("templates:all:archived_false");
            await garnet.KeyDeleteAsync("templates:all:archived_true");
            await garnet.KeyDeleteAsync($"template:detail:{id}");
            if (!string.IsNullOrWhiteSpace(guards.Slug))
                await garnet.KeyDeleteAsync($"template:slug:{guards.Slug}");

            logger.LogInformation("Successfully archived Template [{TemplateId}] and invalidated associated caches.", id);

            return Results.NoContent();
        })
        .WithTags("Templates");
    }
}