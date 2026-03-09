using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Web;
using Kariyer.Mail.Api.Common.Web.Filters;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Templates.CreateTemplate;

internal sealed class CreateTemplateEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("templates", async (
            CreateTemplateRequest request,
            MailDbContext dbContext,
            IConnectionMultiplexer multiplexer,
            CancellationToken ct) =>
        {
            EmailTemplate template = new (request.Name, request.SubjectTemplate, request.HtmlContent);
            
            await dbContext.EmailTemplates.AddAsync(template, ct);
            await dbContext.SaveChangesAsync(ct);
            
            IDatabase garnet = multiplexer.GetDatabase();
            await garnet.KeyDeleteAsync("templates:all:archived_false");
            await garnet.KeyDeleteAsync("templates:all:archived_true");
            
            return Results.Ok(new { TemplateId = template.Id });
        })
        .AddEndpointFilter<ValidationFilter<CreateTemplateRequest>>()
        .WithTags("Templates");
    }
}