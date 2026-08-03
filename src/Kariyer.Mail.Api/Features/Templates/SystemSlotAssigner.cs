using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Templates;

/// <summary>Why an attempt to bind a template to a system slot did not go through.</summary>
public enum SlotAssignmentOutcome
{
    Assigned,
    ClearFailed,      // couldn't release the slug from its previous holder
    SlugRaced         // another request claimed the slug concurrently
}

public sealed record SlotAssignmentResult(SlotAssignmentOutcome Outcome, string? Message = null)
{
    public bool Succeeded => Outcome == SlotAssignmentOutcome.Assigned;
}

/// <summary>
/// Binds a template to a slot's slug. Shared by <c>AssignSystemSlot</c> (bind an existing template)
/// and <c>CreateSlotTemplate</c> (create-and-bind in one call) so the two-phase slug handover
/// exists in exactly one place.
/// </summary>
internal static class SystemSlotAssigner
{
    public static async Task<SlotAssignmentResult> AssignAsync(
        MailDbContext dbContext,
        IConnectionMultiplexer multiplexer,
        ITemplateResolutionService templateService,
        ILogger logger,
        EmailTemplate target,
        string slug,
        CancellationToken ct)
    {
        IDatabase garnet = multiplexer.GetDatabase();

        // Phase 1: clear the existing holder of this slug and save separately, otherwise both rows
        // briefly share the slug and the unique index rejects the write.
        EmailTemplate? existing = await dbContext.EmailTemplates
            .FirstOrDefaultAsync(t => t.Slug == slug && t.Id != target.Id, ct);

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
                return new(SlotAssignmentOutcome.ClearFailed, "Failed to clear existing slot assignment. Please retry.");
            }

            await TemplateCacheKeys.InvalidateListsAsync(garnet);
            await templateService.InvalidateAsync(existingId, existingSlug);
            logger.LogWarning("Slug [{Slug}] moved from template [{OldId}] to [{NewId}].", slug, existingId, target.Id);
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
            return new(SlotAssignmentOutcome.SlugRaced, "Slot was just assigned by another request. Please refresh and retry.");
        }

        await TemplateCacheKeys.InvalidateListsAsync(garnet);
        await templateService.InvalidateAsync(target.Id, slug);

        logger.LogInformation("Template [{TemplateId}] bound to slug [{Slug}].", target.Id, slug);
        return new(SlotAssignmentOutcome.Assigned);
    }
}
