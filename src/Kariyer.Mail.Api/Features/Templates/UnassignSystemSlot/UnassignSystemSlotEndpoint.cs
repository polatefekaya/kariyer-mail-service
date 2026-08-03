using System.Diagnostics;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Templates.UnassignSystemSlot;

internal sealed class UnassignSystemSlotEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("templates/system/slots/{settingsKey}", async (
            string settingsKey,
            MailDbContext dbContext,
            IConnectionMultiplexer multiplexer,
            ITemplateResolutionService templateService,
            ITemplateContextResolver contextResolver,
            ILogger<UnassignSystemSlotEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("UnassignSystemSlot");
            activity?.SetTag("settings.key", settingsKey);

            string? slug = contextResolver.ResolveSlug(settingsKey);

            if (string.IsNullOrWhiteSpace(slug))
                return Results.BadRequest(new { Message = $"Settings key '{settingsKey}' is not configured or is empty." });

            activity?.SetTag("template.slug", slug);

            EmailTemplate? template = await dbContext.EmailTemplates
                .FirstOrDefaultAsync(t => t.Slug == slug, ct);

            if (template == null)
            {
                logger.LogDebug("Unassign slot [{SettingsKey}]: no template has slug [{Slug}], nothing to do.", settingsKey, slug);
                return Results.NoContent();
            }

            string? oldSlug = template.Slug;
            template.UnmarkAsSystemTemplate(); // clears Slug + IsSystemTemplate

            await dbContext.SaveChangesAsync(ct);

            IDatabase garnet = multiplexer.GetDatabase();
            await TemplateCacheKeys.InvalidateListsAsync(garnet);
            await templateService.InvalidateAsync(template.Id, oldSlug);

            logger.LogInformation("Template [{TemplateId}] unassigned from slot [{SettingsKey}] (slug: [{Slug}]).", template.Id, settingsKey, oldSlug);
            return Results.NoContent();
        })
        .WithTags("Templates");
    }
}
