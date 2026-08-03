using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Templating;
using Kariyer.Mail.Api.Common.Web;
using Microsoft.EntityFrameworkCore;

namespace Kariyer.Mail.Api.Features.Templates.Maintenance;

/// <summary>
/// Checks every configured system slot's template against that slot's own vocabulary.
///
/// The fixes elsewhere stop new templates being authored against the wrong variable set; this
/// answers the other half of the question — which templates already in production are silently
/// rendering placeholders as empty strings right now.
/// </summary>
internal sealed class AuditSystemTemplatesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("templates/system/audit", async (
            MailDbContext dbContext,
            ITemplateContextResolver contextResolver,
            CancellationToken ct) =>
        {
            IReadOnlyList<SystemSlotDescriptor> slots = contextResolver.Slots;

            string[] slugs = slots
                .Where(s => !string.IsNullOrWhiteSpace(s.Slug))
                .Select(s => s.Slug)
                .Distinct()
                .ToArray();

            Dictionary<string, EmailTemplate> bySlug = await dbContext.EmailTemplates
                .AsNoTracking()
                .Where(t => t.Slug != null && slugs.Contains(t.Slug))
                .ToDictionaryAsync(t => t.Slug!, ct);

            List<object> report = [];

            foreach (SystemSlotDescriptor slot in slots)
            {
                if (string.IsNullOrWhiteSpace(slot.Slug))
                {
                    report.Add(new { slot.Context, slot.SettingsKey, Status = "Empty" });
                    continue;
                }

                if (!bySlug.TryGetValue(slot.Slug, out EmailTemplate? template))
                {
                    report.Add(new { slot.Context, slot.SettingsKey, Status = "NotFound", slot.Slug });
                    continue;
                }

                TemplateContextDefinition? definition = contextResolver.GetDefinition(slot.Context);
                string[] known = definition?.Placeholders.Select(p => p.Name).ToArray() ?? [];

                TemplateAnalysis analysis = ScribanTemplateAnalyzer.Analyze(
                    ScribanContentNormalizer.Normalize(template.SubjectTemplate),
                    ScribanContentNormalizer.Normalize(template.HtmlContent),
                    known);

                string[] unknownVariables = analysis.ReferencedVariables
                    .Where(v => !known.Contains(v, StringComparer.Ordinal))
                    .OrderBy(v => v, StringComparer.Ordinal)
                    .ToArray();

                bool healthy = !analysis.HasErrors && unknownVariables.Length == 0;

                report.Add(new
                {
                    slot.Context,
                    slot.SettingsKey,
                    Status = healthy ? "Healthy" : "NeedsAttention",
                    TemplateId = template.Id,
                    template.Name,
                    slot.Slug,
                    NeedsNormalization = ScribanContentNormalizer.NeedsNormalization(template.SubjectTemplate)
                                      || ScribanContentNormalizer.NeedsNormalization(template.HtmlContent),
                    SyntaxErrors = analysis.Errors,
                    UnknownVariables = unknownVariables,
                    ExpectedVariables = known
                });
            }

            return Results.Ok(report);
        })
        .WithTags("Templates");
    }
}
