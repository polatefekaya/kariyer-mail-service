using System.Diagnostics;
using System.Text.Json;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;
using Kariyer.Mail.Api.Features.Templates.GetAllTemplates.Contracts;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Templates.GetAllTemplates;

internal sealed class GetAllTemplatesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("templates", async (
            bool? includeArchived,
            string? context,
            MailDbContext dbContext,
            IConnectionMultiplexer multiplexer,
            ITemplateContextResolver contextResolver,
            ILogger<GetAllTemplatesEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("GetAllTemplates");

            string cacheKey = TemplateCacheKeys.AllTemplates(includeArchived ?? false);
            activity?.SetTag("cache.key", cacheKey);
            activity?.SetTag("templates.context_filter", context ?? "none");

            IDatabase garnet = multiplexer.GetDatabase();

            RedisValue cachedData = await garnet.StringGetAsync(cacheKey);
            if (cachedData.HasValue)
            {
                List<TemplateSummaryDto>? cachedTemplates = JsonSerializer.Deserialize<List<TemplateSummaryDto>>(cachedData.ToString());
                if (cachedTemplates != null)
                {
                    logger.LogDebug("Cache HIT for {CacheKey}. Returning {Count} templates.", cacheKey, cachedTemplates.Count);
                    return Results.Ok(FilterByContext(cachedTemplates, context));
                }
            }

            logger.LogDebug("Cache MISS for {CacheKey}. Querying PostgreSQL.", cacheKey);

            IQueryable<EmailTemplate> query = dbContext.EmailTemplates.AsNoTracking();
            if (includeArchived != true)
            {
                query = query.Where(t => !t.IsArchived);
            }

            // Project the raw columns first: ResolveContext is a dictionary lookup and cannot be
            // translated to SQL, so the DTO has to be built after materialisation.
            var rows = await query
                .OrderByDescending(e => e.CreatedAt)
                .Select(e => new
                {
                    e.Id,
                    e.Name,
                    e.IsArchived,
                    e.IsSystemTemplate,
                    e.Slug,
                    e.CreatedAt,
                    e.UpdatedAt
                })
                .ToListAsync(ct);

            List<TemplateSummaryDto> templates = rows
                .Select(e => new TemplateSummaryDto(
                    e.Id,
                    e.Name,
                    e.IsArchived,
                    e.IsSystemTemplate,
                    e.Slug,
                    contextResolver.ResolveContext(e.Slug),
                    e.CreatedAt,
                    e.UpdatedAt))
                .ToList();

            string serializedData = JsonSerializer.Serialize(templates);
            await garnet.StringSetAsync(cacheKey, serializedData, TimeSpan.FromHours(1));

            logger.LogInformation("Fetched {Count} templates from database and updated Garnet cache.", templates.Count);

            return Results.Ok(FilterByContext(templates, context));
        })
        .WithTags("Templates");
    }

    // Applied after the cache read so the cached blob stays keyed on includeArchived alone.
    private static List<TemplateSummaryDto> FilterByContext(List<TemplateSummaryDto> templates, string? context) =>
        string.IsNullOrWhiteSpace(context)
            ? templates
            : templates.Where(t => string.Equals(t.Context, context, StringComparison.Ordinal)).ToList();
}
