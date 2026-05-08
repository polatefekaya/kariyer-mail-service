using System.Diagnostics;
using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Templates.AssignSystemSlot;

internal sealed class AssignSystemSlotEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // settingsKey is the property name on EmailTemplateSettings, e.g. "AccountCreatedTemplateSlug"
        app.MapPut("templates/system/slots/{settingsKey}", async (
            string settingsKey,
            AssignSystemSlotRequest request,
            MailDbContext dbContext,
            IConnectionMultiplexer multiplexer,
            ITemplateResolutionService templateService,
            IOptions<EmailTemplateSettings> settingsOptions,
            ILogger<AssignSystemSlotEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("AssignSystemSlot");
            activity?.SetTag("settings.key", settingsKey);

            // Resolve slug from env/config via the settingsKey property name
            EmailTemplateSettings s = settingsOptions.Value;
            string? slug = ResolveSlug(s, settingsKey);

            if (string.IsNullOrWhiteSpace(slug))
                return Results.BadRequest(new { Message = $"Settings key '{settingsKey}' is not configured or is empty." });

            if (!Ulid.TryParse(request.TemplateId, out Ulid templateId))
                return Results.BadRequest(new { Message = "Invalid TemplateId format." });

            activity?.SetTag("template.slug", slug);
            activity?.SetTag("template.id", templateId.ToString());

            // Fetch target early so we can validate before touching existing assignment
            EmailTemplate? target = await dbContext.EmailTemplates
                .FirstOrDefaultAsync(t => t.Id == templateId, ct);

            if (target == null)
                return Results.NotFound(new { Message = $"Template [{templateId}] not found." });

            if (target.IsArchived)
                return Results.BadRequest(new { Message = $"Template [{templateId}] is archived and cannot be assigned to a system slot." });

            if (target.Slug != null && target.Slug != slug)
                return Results.Conflict(new { Message = $"Template [{templateId}] is already assigned to another slot (slug: '{target.Slug}'). Unassign it first." });

            // Phase 1: clear the existing holder of this slug and save separately to avoid
            // a unique constraint violation when both templates briefly share the same slug.
            EmailTemplate? existing = await dbContext.EmailTemplates
                .FirstOrDefaultAsync(t => t.Slug == slug && t.Id != templateId, ct);

            IDatabase garnet = multiplexer.GetDatabase();

            if (existing != null)
            {
                string? existingSlug = existing.Slug;
                Ulid existingId = existing.Id;
                existing.UnmarkAsSystemTemplate();
                try
                {
                    await dbContext.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex)
                {
                    logger.LogError(ex, "Phase-1 save failed while clearing slug [{Slug}] from template [{OldId}].", slug, existingId);
                    return Results.Problem("Failed to clear existing slot assignment. Please retry.", statusCode: 500);
                }
                await garnet.KeyDeleteAsync("templates:all:archived_false");
                await garnet.KeyDeleteAsync("templates:all:archived_true");
                await templateService.InvalidateAsync(existingId, existingSlug);
                logger.LogWarning("Slug [{Slug}] moved from template [{OldId}] to [{NewId}].", slug, existingId, templateId);
            }

            // Phase 2: assign slug to target
            target.MarkAsSystemTemplate();
            target.SetSlug(slug);

            try
            {
                await dbContext.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_EmailTemplates_Slug") == true)
            {
                logger.LogWarning(ex, "Concurrent assignment conflict for slug [{Slug}].", slug);
                return Results.Conflict(new { Message = $"Slot [{settingsKey}] was just assigned by another request. Please refresh and retry." });
            }

            await garnet.KeyDeleteAsync("templates:all:archived_false");
            await garnet.KeyDeleteAsync("templates:all:archived_true");
            await templateService.InvalidateAsync(templateId, slug);

            logger.LogInformation("Template [{TemplateId}] assigned to slot [{SettingsKey}] with slug [{Slug}].", templateId, settingsKey, slug);
            return Results.NoContent();
        })
        .WithTags("Templates");
    }

    private static string? ResolveSlug(EmailTemplateSettings s, string settingsKey) => settingsKey switch
    {
        nameof(s.AccountCreatedTemplateSlug)              => s.AccountCreatedTemplateSlug,
        nameof(s.AccountCompletedTemplateSlug)            => s.AccountCompletedTemplateSlug,
        nameof(s.AccountApprovedTemplateSlug)             => s.AccountApprovedTemplateSlug,
        nameof(s.AccountRejectedTemplateSlug)             => s.AccountRejectedTemplateSlug,
        nameof(s.AccountFrozenTemplateSlug)               => s.AccountFrozenTemplateSlug,
        nameof(s.AccountDeletedTemplateSlug)              => s.AccountDeletedTemplateSlug,
        nameof(s.AccountDidNotCompletedStep1TemplateSlug) => s.AccountDidNotCompletedStep1TemplateSlug,
        nameof(s.AccountDidNotCompletedStep2TemplateSlug) => s.AccountDidNotCompletedStep2TemplateSlug,
        nameof(s.AccountDidNotCompletedStep3TemplateSlug) => s.AccountDidNotCompletedStep3TemplateSlug,
        nameof(s.AdminCompanyCompletedTemplateSlug)       => s.AdminCompanyCompletedTemplateSlug,
        _ => null
    };
}
