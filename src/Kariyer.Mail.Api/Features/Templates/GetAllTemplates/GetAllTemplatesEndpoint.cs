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
            MailDbContext dbContext,
            IConnectionMultiplexer multiplexer,
            ILogger<GetAllTemplatesEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("GetAllTemplates");
            
            string cacheKey = $"templates:all:archived_{includeArchived ?? false}";
            activity?.SetTag("cache.key", cacheKey);

            IDatabase garnet = multiplexer.GetDatabase();

            RedisValue cachedData = await garnet.StringGetAsync(cacheKey);
            if (cachedData.HasValue)
            {
                List<TemplateSummaryDto>? cachedTemplates = JsonSerializer.Deserialize<List<TemplateSummaryDto>>(cachedData.ToString());
                if (cachedTemplates != null) 
                {
                    logger.LogDebug("Cache HIT for {CacheKey}. Returning {Count} templates.", cacheKey, cachedTemplates.Count);
                    return Results.Ok(cachedTemplates);
                }
            }

            logger.LogDebug("Cache MISS for {CacheKey}. Querying PostgreSQL.", cacheKey);

            IQueryable<EmailTemplate> query = dbContext.EmailTemplates.AsNoTracking();
            if (includeArchived != true)
            {
                query = query.Where(t => !t.IsArchived);
            }

            List<TemplateSummaryDto> templates = await query
                .OrderByDescending(e => e.CreatedAt)
                .Select(e => new TemplateSummaryDto(
                    e.Id, 
                    e.Name, 
                    e.IsArchived, 
                    e.CreatedAt, 
                    e.UpdatedAt
                ))
                .ToListAsync(ct);
                
            string serializedData = JsonSerializer.Serialize(templates);
            await garnet.StringSetAsync(cacheKey, serializedData, TimeSpan.FromHours(1));

            logger.LogInformation("Fetched {Count} templates from database and updated Garnet cache.", templates.Count);

            return Results.Ok(templates);
        })
        .WithTags("Templates");
    }
}