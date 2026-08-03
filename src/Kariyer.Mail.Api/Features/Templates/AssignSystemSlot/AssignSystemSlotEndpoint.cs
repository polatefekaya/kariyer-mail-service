using System.Diagnostics;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;
using Microsoft.EntityFrameworkCore;
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
            ITemplateContextResolver contextResolver,
            ILogger<AssignSystemSlotEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("AssignSystemSlot");
            activity?.SetTag("settings.key", settingsKey);

            string? slug = contextResolver.ResolveSlug(settingsKey);

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

            SlotAssignmentResult result = await SystemSlotAssigner.AssignAsync(
                dbContext, multiplexer, templateService, logger, target, slug, ct);

            return result.Outcome switch
            {
                SlotAssignmentOutcome.Assigned    => Results.NoContent(),
                SlotAssignmentOutcome.SlugRaced   => Results.Conflict(new { Message = $"Slot [{settingsKey}] was just assigned by another request. Please refresh and retry." }),
                _                                 => Results.Problem(result.Message, statusCode: 500)
            };
        })
        .WithTags("Templates");
    }
}
