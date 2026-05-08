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

            // If another template currently holds this slug, clear it first
            EmailTemplate? existing = await dbContext.EmailTemplates
                .FirstOrDefaultAsync(t => t.Slug == slug, ct);

            if (existing != null && existing.Id != templateId)
            {
                string? existingSlug = existing.Slug;
                existing.UnmarkAsSystemTemplate(); // clears slug + unmarks
                await templateService.InvalidateAsync(existing.Id, existingSlug);
                logger.LogWarning("Slug [{Slug}] moved from template [{OldId}] to [{NewId}].", slug, existing.Id, templateId);
            }

            EmailTemplate? target = await dbContext.EmailTemplates
                .FirstOrDefaultAsync(t => t.Id == templateId, ct);

            if (target == null)
                return Results.NotFound(new { Message = $"Template [{templateId}] not found." });

            if (target.Slug != null && target.Slug != slug)
                return Results.Conflict(new { Message = $"Template [{templateId}] is already assigned to another slot (slug: '{target.Slug}'). Unassign it first." });

            target.MarkAsSystemTemplate();
            target.SetSlug(slug);

            await dbContext.SaveChangesAsync(ct);

            IDatabase garnet = multiplexer.GetDatabase();
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
