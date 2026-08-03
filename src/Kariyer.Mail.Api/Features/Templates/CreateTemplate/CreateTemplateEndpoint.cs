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
            ITemplateContextResolver contextResolver,
            ILogger<CreateTemplateEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("CreateTemplate");

            // Context only selects the vocabulary used for unknown-variable warnings — it is not
            // persisted. A template's real context is derived from its slug once it is slot-bound.
            string context = request.Context ?? TemplateContextRegistry.BulkEmailContext;
            activity?.SetTag("template.context", context);

            if (!TemplateContentValidator.TryPrepare(
                    request.SubjectTemplate, request.HtmlContent, context, contextResolver,
                    out PreparedTemplateContent prepared, out IResult? error))
            {
                logger.LogWarning("Create rejected: template content has Scriban syntax errors.");
                return error!;
            }

            EmailTemplate template = new(request.Name, prepared.Subject, prepared.Html);

            await dbContext.EmailTemplates.AddAsync(template, ct);
            await dbContext.SaveChangesAsync(ct);

            activity?.SetTag("template.id", template.Id.ToString());

            IDatabase garnet = multiplexer.GetDatabase();
            await TemplateCacheKeys.InvalidateListsAsync(garnet);

            logger.LogInformation("Created new Template [{TemplateId}] with Name: '{TemplateName}'.", template.Id, template.Name);

            return Results.Ok(new { TemplateId = template.Id, Warnings = prepared.Warnings });
        })
        .AddEndpointFilter<ValidationFilter<CreateTemplateRequest>>()
        .WithTags("Templates");
    }
}
