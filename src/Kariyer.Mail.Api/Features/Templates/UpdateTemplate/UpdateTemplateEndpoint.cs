using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Web;
using Kariyer.Mail.Api.Common.Web.Filters;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Templates.UpdateTemplate;

internal sealed class UpdateTemplateEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("templates/{id:ulid}", async (
            Ulid id,
            UpdateTemplateRequest request,
            MailDbContext dbContext,
            IConnectionMultiplexer multiplexer,
            CancellationToken ct) =>
        {
            IDatabase garnet = multiplexer.GetDatabase();
            
            EmailTemplate? template = await dbContext.EmailTemplates
                .FirstOrDefaultAsync(t => t.Id == id, ct);

            if (template == null) return Results.NotFound();
            
            if (template.IsArchived)
            {
                return Results.BadRequest(new { Message = "Cannot update an archived template. Unarchive it first." });
            }

            bool isLockedByPastJobs = await dbContext.EmailJobs.AnyAsync(j => j.TemplateId == id, ct);

            if (isLockedByPastJobs)
            {
                return Results.Conflict(new 
                { 
                    Message = "This template has already been used in an active or historical email job. " +
                              "Its content is locked to preserve the audit trail. Please duplicate/clone this template to make changes." 
                });
            }

            template.Update(request.Name, request.SubjectTemplate, request.HtmlContent);

            await dbContext.SaveChangesAsync(ct);
            
            await garnet.KeyDeleteAsync("templates:all:archived_false");
            await garnet.KeyDeleteAsync("templates:all:archived_true");
            await garnet.KeyDeleteAsync($"template:detail:{id}");

            return Results.NoContent();
        })
        .AddEndpointFilter<ValidationFilter<UpdateTemplateRequest>>()
        .WithTags("Templates");
    }
}