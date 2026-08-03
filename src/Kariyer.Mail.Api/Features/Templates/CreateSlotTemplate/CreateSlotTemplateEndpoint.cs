using System.Diagnostics;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;
using Kariyer.Mail.Api.Common.Web.Filters;
using Microsoft.EntityFrameworkCore.Storage;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Templates.CreateSlotTemplate;

/// <summary>
/// Creates a template already bound to a system slot.
///
/// Before this existed, an automated template could only be authored in the bulk-email tab — which
/// advertises a completely different variable vocabulary — and then assigned afterwards. That is
/// the root cause of automated mails going out with empty fields: authors wrote
/// <c>{{ FirstName }}</c> for a slot whose consumer only ever supplies <c>{{ FullName }}</c>.
/// Creating in place means the content is validated against the slot's own vocabulary from the
/// first save, and a failure part-way through cannot leave an orphaned template behind.
/// </summary>
internal sealed class CreateSlotTemplateEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("templates/system/slots/{settingsKey}/template", async (
            string settingsKey,
            CreateSlotTemplateRequest request,
            MailDbContext dbContext,
            IConnectionMultiplexer multiplexer,
            ITemplateResolutionService templateService,
            ITemplateContextResolver contextResolver,
            ILogger<CreateSlotTemplateEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("CreateSlotTemplate");
            activity?.SetTag("settings.key", settingsKey);

            if (!TemplateContextRegistry.TryGetBySettingsKey(settingsKey, out TemplateContextDefinition definition))
                return Results.NotFound(new { Message = $"Unknown system slot '{settingsKey}'." });

            string? slug = contextResolver.ResolveSlug(settingsKey);
            if (string.IsNullOrWhiteSpace(slug))
                return Results.BadRequest(new { Message = $"Settings key '{settingsKey}' is not configured or is empty." });

            activity?.SetTag("template.context", definition.Context);
            activity?.SetTag("template.slug", slug);

            if (!TemplateContentValidator.TryPrepare(
                    request.SubjectTemplate, request.HtmlContent, definition.Context, contextResolver,
                    out PreparedTemplateContent prepared, out IResult? error))
            {
                logger.LogWarning("Create-in-slot rejected for [{SettingsKey}]: Scriban syntax errors.", settingsKey);
                return error!;
            }

            EmailTemplate template = new(request.Name, prepared.Subject, prepared.Html);

            // One transaction so a failed hand-over cannot leave a template that belongs to nothing.
            await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync(ct);

            await dbContext.EmailTemplates.AddAsync(template, ct);
            await dbContext.SaveChangesAsync(ct);

            SlotAssignmentResult assignment = await SystemSlotAssigner.AssignAsync(
                dbContext, multiplexer, templateService, logger, template, slug, ct);

            if (!assignment.Succeeded)
            {
                await transaction.RollbackAsync(ct);
                logger.LogWarning("Create-in-slot for [{SettingsKey}] rolled back: {Outcome}.", settingsKey, assignment.Outcome);

                return assignment.Outcome == SlotAssignmentOutcome.SlugRaced
                    ? Results.Conflict(new { Message = $"Slot [{settingsKey}] was just assigned by another request. Please refresh and retry." })
                    : Results.Problem(assignment.Message, statusCode: 500);
            }

            await transaction.CommitAsync(ct);

            // The assigner already dropped the cache entries, but it did so inside the transaction —
            // repeat now that the rows are actually visible to other readers.
            await TemplateCacheKeys.InvalidateListsAsync(multiplexer.GetDatabase());
            await templateService.InvalidateAsync(template.Id, slug);

            activity?.SetTag("template.id", template.Id.ToString());
            logger.LogInformation(
                "Created Template [{TemplateId}] '{TemplateName}' directly in slot [{SettingsKey}] (context: {Context}).",
                template.Id, template.Name, settingsKey, definition.Context);

            return Results.Ok(new
            {
                TemplateId = template.Id,
                Context = definition.Context,
                Slug = slug,
                Warnings = prepared.Warnings
            });
        })
        .AddEndpointFilter<ValidationFilter<CreateSlotTemplateRequest>>()
        .WithTags("Templates");
    }
}
