using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Web;
using Kariyer.Mail.Api.Features.Templates.GetTemplate.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kariyer.Mail.Api.Features.Templates.GetSystemTemplates;

internal sealed class GetSystemTemplatesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("templates/system", async (
            MailDbContext dbContext,
            ITemplateContextResolver contextResolver,
            CancellationToken ct) =>
        {
            IReadOnlyList<SystemSlotDescriptor> slots = contextResolver.Slots;

            // Collect all configured slugs for a single bulk DB query
            string[] slugs = slots
                .Where(slot => !string.IsNullOrWhiteSpace(slot.Slug))
                .Select(slot => slot.Slug)
                .Distinct()
                .ToArray();

            Dictionary<string, EmailTemplate> templates = await dbContext.EmailTemplates
                .AsNoTracking()
                .Where(t => t.Slug != null && slugs.Contains(t.Slug))
                .ToDictionaryAsync(t => t.Slug!, ct);

            List<SystemTemplateSlotDto> result = new(slots.Count);

            foreach (SystemSlotDescriptor slot in slots)
            {
                if (string.IsNullOrWhiteSpace(slot.Slug))
                {
                    result.Add(new(slot.Context, slot.Description, slot.SettingsKey, SystemTemplateStatus.Empty, null));
                    continue;
                }

                if (!templates.TryGetValue(slot.Slug, out EmailTemplate? template))
                {
                    result.Add(new(slot.Context, slot.Description, slot.SettingsKey, SystemTemplateStatus.NotFound, null));
                    continue;
                }

                TemplateDetailDto dto = new(
                    template.Id,
                    template.Name,
                    template.SubjectTemplate,
                    template.HtmlContent,
                    template.IsArchived,
                    template.IsSystemTemplate,
                    template.Slug,
                    slot.Context,
                    template.CreatedAt,
                    template.UpdatedAt);

                result.Add(new(slot.Context, slot.Description, slot.SettingsKey, SystemTemplateStatus.Configured, dto));
            }

            return Results.Ok(result);
        })
        .WithTags("Templates");
    }
}
