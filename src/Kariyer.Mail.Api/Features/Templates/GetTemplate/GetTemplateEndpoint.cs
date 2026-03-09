using System.Diagnostics;
using System.Text.Json;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;
using Kariyer.Mail.Api.Features.Templates.GetTemplate.Contracts;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Templates.GetTemplate;

internal sealed class GetTemplateEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("templates/{id:ulid}", async (
            Ulid id,
            MailDbContext dbContext,
            IConnectionMultiplexer multiplexer,
            ILogger<GetTemplateEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("GetSingleTemplate");
            activity?.SetTag("template.id", id.ToString());

            string cacheKey = $"template:detail:{id}";
            IDatabase garnet = multiplexer.GetDatabase();

            RedisValue cachedData = await garnet.StringGetAsync(cacheKey);
            if (cachedData.HasValue)
            {
                TemplateDetailDto? cachedTemplate = JsonSerializer.Deserialize<TemplateDetailDto>(cachedData.ToString());
                if (cachedTemplate != null) 
                {
                    logger.LogDebug("Cache HIT for Template [{TemplateId}]", id);
                    return Results.Ok(cachedTemplate);
                }
            }

            logger.LogDebug("Cache MISS for Template [{TemplateId}]. Hitting PostgreSQL.", id);

            TemplateDetailDto? template = await dbContext.EmailTemplates
                .AsNoTracking()
                .Where(t => t.Id == id)
                .Select(t => new TemplateDetailDto(t.Id, t.Name, t.SubjectTemplate, t.HtmlContent, t.IsArchived, t.CreatedAt))
                .FirstOrDefaultAsync(ct);

            if (template == null) 
            {
                logger.LogWarning("Fetch failed: Template [{TemplateId}] not found.", id);
                return Results.NotFound();
            }

            string serializedData = JsonSerializer.Serialize(template);
            await garnet.StringSetAsync(cacheKey, serializedData, TimeSpan.FromHours(24));

            return Results.Ok(template);
        })
        .WithTags("Templates");
    }
}