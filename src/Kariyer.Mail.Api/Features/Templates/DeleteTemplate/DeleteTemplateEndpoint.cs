using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Web;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Templates.DeleteTemplate;

internal sealed class DeleteTemplateEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("templates/{id:ulid}", async (
            Ulid id,
            MailDbContext dbContext,
            IConnectionMultiplexer multiplexer,
            CancellationToken ct) =>
        {
            IDatabase garnet = multiplexer.GetDatabase();
            bool isReferenced = await dbContext.EmailJobs.AnyAsync(j => j.TemplateId == id, ct);
            
            if (isReferenced)
            {
                return Results.Conflict(new 
                { 
                    Message = "This template has been used in one or more bulk email jobs. It cannot be deleted to preserve the historical audit trail. Consider archiving it instead." 
                });
            }

            int deletedCount = await dbContext.EmailTemplates
                .Where(t => t.Id == id)
                .ExecuteDeleteAsync(ct);
                
            await garnet.KeyDeleteAsync("templates:all:archived_false");
            await garnet.KeyDeleteAsync("templates:all:archived_true");
            await garnet.KeyDeleteAsync($"template:detail:{id}");

            if (deletedCount == 0)
            {
                return Results.NotFound(new { Message = "Template not found." });
            }

            return Results.NoContent();
        })
        .WithTags("Templates");
    }
}