using System.Diagnostics;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
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
            ILogger<CreateTemplateEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("CreateTemplate");

            EmailTemplate template = new(request.Name, request.SubjectTemplate, request.HtmlContent);
            
            await dbContext.EmailTemplates.AddAsync(template, ct);
            await dbContext.SaveChangesAsync(ct);
            
            activity?.SetTag("template.id", template.Id.ToString());
            
            IDatabase garnet = multiplexer.GetDatabase();
            await garnet.KeyDeleteAsync("templates:all:archived_false");
            await garnet.KeyDeleteAsync("templates:all:archived_true");
            
            logger.LogInformation("Created new Template [{TemplateId}] with Name: '{TemplateName}'.", template.Id, template.Name);

            return Results.Ok(new { TemplateId = template.Id });
        })
        .AddEndpointFilter<ValidationFilter<CreateTemplateRequest>>()
        .WithTags("Templates");
    }
}