using System.Diagnostics;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;
using Kariyer.Mail.Api.Common.Web.Filters;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Templates.BulkDeleteTemplates;

internal sealed class BulkDeleteTemplatesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("templates/bulk-delete", async (
            BulkDeleteTemplatesRequest request,
            MailDbContext dbContext,
            IConnectionMultiplexer multiplexer,
            ITemplateResolutionService templateService,
            ILogger<BulkDeleteTemplatesEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("BulkDeleteTemplates");
            activity?.SetTag("request.count", request.TemplateIds.Length);

            Ulid[] jobLockedIds = await dbContext.EmailJobs
                .Where(j => j.TemplateId != null && request.TemplateIds.Contains(j.TemplateId.Value))
                .Select(j => j.TemplateId!.Value)
                .Distinct()
                .ToArrayAsync(ct);

            Ulid[] systemLockedIds = await dbContext.EmailTemplates
                .Where(t => t.IsSystemTemplate && request.TemplateIds.Contains(t.Id))
                .Select(t => t.Id)
                .ToArrayAsync(ct);

            Ulid[] lockedTemplateIds = jobLockedIds.Union(systemLockedIds).ToArray();

            Ulid[] safeToDeleteIds = request.TemplateIds
                .Except(lockedTemplateIds)
                .ToArray();

            int deletedCount = 0;

            if (safeToDeleteIds.Length > 0)
            {
                deletedCount = await dbContext.EmailTemplates
                    .Where(t => safeToDeleteIds.Contains(t.Id))
                    .ExecuteDeleteAsync(ct);
            }

            if (deletedCount > 0)
            {
                IDatabase garnet = multiplexer.GetDatabase();
                await garnet.KeyDeleteAsync("templates:all:archived_false");
                await garnet.KeyDeleteAsync("templates:all:archived_true");
                await Task.WhenAll(safeToDeleteIds.Select(id => templateService.InvalidateTemplateCacheAsync(id)));
            }

            logger.LogInformation("Bulk delete complete. Requested: {Requested}, Deleted: {Deleted}, Locked: {Locked}",
                request.TemplateIds.Length, deletedCount, lockedTemplateIds.Length);

            if (lockedTemplateIds.Length > 0)
            {
                logger.LogWarning("Bulk delete skipped {LockedCount} templates because they are actively referenced by Email Jobs.", lockedTemplateIds.Length);
            }

            return Results.Ok(new
            { 
                RequestedCount = request.TemplateIds.Length,
                DeletedCount = deletedCount,
                LockedCount = lockedTemplateIds.Length,
                LockedIds = lockedTemplateIds
            });
        })
        .AddEndpointFilter<ValidationFilter<BulkDeleteTemplatesRequest>>()
        .WithTags("Templates");
    }
}