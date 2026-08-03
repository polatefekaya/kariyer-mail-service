using System.Diagnostics;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Templating;
using Kariyer.Mail.Api.Common.Web;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Templates.Maintenance;

/// <summary>
/// One-shot repair for templates saved before the editor stopped HTML-encoding Scriban delimiters.
/// Those rows render fine in the preview (which used to decode on the fly) and fail at send time,
/// so they have to be rewritten rather than left to the defensive normalisation in the consumer.
/// </summary>
internal sealed class NormalizeTemplatesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("templates/maintenance/normalize", async (
            bool? dryRun,
            MailDbContext dbContext,
            IConnectionMultiplexer multiplexer,
            ITemplateResolutionService templateService,
            ILogger<NormalizeTemplatesEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("NormalizeTemplates");

            bool preview = dryRun ?? true;   // default to dry run: this rewrites content in place
            activity?.SetTag("maintenance.dry_run", preview);

            List<EmailTemplate> templates = await dbContext.EmailTemplates.ToListAsync(ct);

            List<object> affected = [];

            foreach (EmailTemplate template in templates)
            {
                string subject = ScribanContentNormalizer.Normalize(template.SubjectTemplate);
                string html = ScribanContentNormalizer.Normalize(template.HtmlContent);

                bool subjectChanged = !string.Equals(subject, template.SubjectTemplate, StringComparison.Ordinal);
                bool htmlChanged = !string.Equals(html, template.HtmlContent, StringComparison.Ordinal);

                if (!subjectChanged && !htmlChanged) continue;

                affected.Add(new
                {
                    TemplateId = template.Id,
                    template.Name,
                    template.Slug,
                    SubjectChanged = subjectChanged,
                    HtmlChanged = htmlChanged
                });

                if (preview) continue;

                template.Update(template.Name, subject, html);
            }

            if (!preview && affected.Count > 0)
            {
                await dbContext.SaveChangesAsync(ct);

                // The slug/detail caches hold a 24h TTL — without this, consumers keep serving the
                // mangled copies for a full day after the repair.
                await TemplateCacheKeys.InvalidateListsAsync(multiplexer.GetDatabase());

                foreach (EmailTemplate template in templates)
                    await templateService.InvalidateAsync(template.Id, template.Slug);

                logger.LogWarning("Normalized {Count} template(s) and invalidated their caches.", affected.Count);
            }
            else
            {
                logger.LogInformation("Normalization dry run: {Count} of {Total} template(s) would change.",
                    affected.Count, templates.Count);
            }

            return Results.Ok(new
            {
                DryRun = preview,
                Scanned = templates.Count,
                AffectedCount = affected.Count,
                Affected = affected
            });
        })
        .WithTags("Templates");
    }
}
