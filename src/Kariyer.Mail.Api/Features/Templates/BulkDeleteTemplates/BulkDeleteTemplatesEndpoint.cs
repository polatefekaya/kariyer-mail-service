using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Web;
using Kariyer.Mail.Api.Common.Web.Filters;
using Microsoft.EntityFrameworkCore;

namespace Kariyer.Mail.Api.Features.Templates.BulkDeleteTemplates;

internal sealed class BulkDeleteTemplatesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("templates/bulk-delete", async (
            BulkDeleteTemplatesRequest request,
            MailDbContext dbContext,
            CancellationToken ct) =>
        {
            Ulid[] lockedTemplateIds = await dbContext.EmailJobs
                .Where(j => j.TemplateId != null && request.TemplateIds.Contains(j.TemplateId.Value))
                .Select(j => j.TemplateId!.Value)
                .Distinct()
                .ToArrayAsync(ct);

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