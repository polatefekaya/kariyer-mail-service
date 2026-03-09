using System.Diagnostics;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;
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

            logger.LogInformation("Successfully archived Template [{TemplateId}] and invalidated associated caches.", id);

            return Results.NoContent();
        })
        .WithTags("Templates");
    }
}