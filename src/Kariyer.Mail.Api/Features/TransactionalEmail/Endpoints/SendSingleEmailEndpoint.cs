using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Web;
using Kariyer.Mail.Api.Features.DispatchEmail;
using Kariyer.Mail.Api.Features.TransactionalEmail.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.TransactionalEmail.Endpoints;

internal sealed class SendSingleEmailEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("transactional/send", async (
            HttpRequest req,
            SendSingleEmailRequest request,
            MailDbContext dbContext,
            IPublishEndpoint publishEndpoint,
            IConnectionMultiplexer multiplexer,
            CancellationToken ct) =>
        {
            string? idempotencyKey = req.Headers["X-Idempotency-Key"];
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return Results.BadRequest(new { Message = "X-Idempotency-Key header is strictly required for transactional emails." });
            }

            IDatabase garnet = multiplexer.GetDatabase();
            bool isFirstRequest = await garnet.StringSetAsync(
                $"idempotency:tx:{idempotencyKey}", 
                "locked", 
                TimeSpan.FromHours(24), 
                When.NotExists);

            if (!isFirstRequest)
            {
                return Results.Conflict(new { Message = "Duplicate request detected and blocked." });
            }

            string finalSubject = request.Subject ?? string.Empty;
            string finalBody = request.BodyTemplate ?? string.Empty;

            if (request.TemplateId.HasValue)
            {
                EmailTemplate? template = await dbContext.EmailTemplates
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == request.TemplateId.Value, ct);

                if (template != null)
                {
                    finalSubject = template.SubjectTemplate;
                    finalBody = template.HtmlContent;
                }
                else
                {
                    return Results.BadRequest(new { Message = $"Template [{request.TemplateId.Value}] not found." });
                }
            }

            if (string.IsNullOrWhiteSpace(finalSubject) || string.IsNullOrWhiteSpace(finalBody))
            {
                return Results.BadRequest(new { Message = "Subject and Body must be provided either directly or via a valid TemplateId." });
            }

            EmailTarget target = new(
                jobId: null,
                recipientUserId: request.UserId,
                recipientEmail: request.Email,
                subject: finalSubject,
                body: finalBody
            );

            await dbContext.EmailTargets.AddAsync(target, ct);

            Dictionary<string, string> templateData = request.TemplateData ?? new Dictionary<string, string>();
            if (!templateData.ContainsKey("Email")) 
            {
                templateData.Add("Email", target.RecipientEmail);
            }

            DispatchEmailCommand command = new (
                JobId: null,
                TargetId: target.Id,
                Email: target.RecipientEmail,
                Subject: finalSubject,
                RawTemplate: finalBody,
                TemplateData: templateData
            );

            await publishEndpoint.Publish(command, ct);

            await dbContext.SaveChangesAsync(ct);

            return Results.Accepted($"transactional/status/{target.Id}", new { TargetId = target.Id });
        })
        .WithTags("Transactional Email");
    }
}