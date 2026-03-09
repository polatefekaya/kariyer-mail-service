using System.Text.Json;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
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
            CancellationToken ct) =>
        {
            string cacheKey = $"templates:all:archived_{includeArchived ?? false}";
            IDatabase garnet = multiplexer.GetDatabase();

            RedisValue cachedData = await garnet.StringGetAsync(cacheKey);
            if (cachedData.HasValue)
            {
                List<TemplateSummaryDto>? cachedTemplates = JsonSerializer.Deserialize<List<TemplateSummaryDto>>(cachedData.ToString());
                if (cachedTemplates != null) return Results.Ok(cachedTemplates);
            }

            IQueryable<EmailTemplate> query = dbContext.EmailTemplates.AsNoTracking();
            if (includeArchived != true)
            {
                query = query.Where(t => !t.IsArchived);
            }

            List<TemplateSummaryDto> templates = await query
                .Select(t => new TemplateSummaryDto(t.Id, t.Name, t.IsArchived, t.CreatedAt, t.UpdatedAt))
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync(ct);
                
            string serializedData = JsonSerializer.Serialize(templates);
            await garnet.StringSetAsync(cacheKey, serializedData, TimeSpan.FromHours(1));

            return Results.Ok(templates);
        })
        .WithTags("Templates");
    }
}
