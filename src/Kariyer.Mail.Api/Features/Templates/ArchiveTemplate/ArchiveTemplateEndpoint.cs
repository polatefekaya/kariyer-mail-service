using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Web;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Templates.ArchiveTemplate;

internal sealed class ArchiveTemplateEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("templates/{id:ulid}/archive", async (
            Ulid id,
            MailDbContext dbContext,
            IConnectionMultiplexer multiplexer,
            CancellationToken ct) =>
        {
            IDatabase garnet = multiplexer.GetDatabase();
            
            int updatedCount = await dbContext.EmailTemplates
                .Where(t => t.Id == id && !t.IsArchived)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.IsArchived, true)
                    .SetProperty(t => t.UpdatedAt, DateTime.UtcNow),
                ct);
                
            await garnet.KeyDeleteAsync("templates:all:archived_false");
            await garnet.KeyDeleteAsync("templates:all:archived_true");
            await garnet.KeyDeleteAsync($"template:detail:{id}");

            if (updatedCount == 0)
            {
                return Results.NotFound(new { Message = "Template not found or already archived." });
            }

            return Results.NoContent();
        })
        .WithTags("Templates");
    }
}