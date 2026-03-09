using System.Text.Json;
using Kariyer.Mail.Api.Common.Persistence;
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
            CancellationToken ct) =>
        {
            string cacheKey = $"template:detail:{id}";
            IDatabase garnet = multiplexer.GetDatabase();

            RedisValue cachedData = await garnet.StringGetAsync(cacheKey);
            if (cachedData.HasValue)
            {
                TemplateDetailDto? cachedTemplate = JsonSerializer.Deserialize<TemplateDetailDto>(cachedData.ToString());
                if (cachedTemplate != null) return Results.Ok(cachedTemplate);
            }

            TemplateDetailDto? template = await dbContext.EmailTemplates
                .AsNoTracking()
                .Where(t => t.Id == id)
                .Select(t => new TemplateDetailDto(t.Id, t.Name, t.SubjectTemplate, t.HtmlContent, t.IsArchived, t.CreatedAt))
                .FirstOrDefaultAsync(ct);

            if (template == null) return Results.NotFound();

            string serializedData = JsonSerializer.Serialize(template);
            await garnet.StringSetAsync(cacheKey, serializedData, TimeSpan.FromHours(24));

            return Results.Ok(template);
        })
        .WithTags("Templates");
    }
}
