using System.Diagnostics;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Templating;
using Kariyer.Mail.Api.Common.Web;
using Kariyer.Mail.Api.Features.BulkEmail.Contracts;
using MassTransit;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.BulkEmail.Endpoints;

internal sealed class StartBulkJobEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("jobs/bulk", async (
            HttpRequest req,
            CreateBulkEmailRequest request,
            MailDbContext dbContext,
            IPublishEndpoint publishEndpoint,
            IConnectionMultiplexer multiplexer,
            ILogger<StartBulkJobEndpoint> logger,
            CancellationToken ct) =>
        {
            using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("StartBulkJob");
            
            string? idempotencyKey = req.Headers["X-Idempotency-Key"];
            activity?.SetTag("http.idempotency_key", idempotencyKey);

            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                logger.LogWarning("Rejected bulk job request: Missing X-Idempotency-Key header.");
                return Results.BadRequest(new { Message = "X-Idempotency-Key header is strictly required." });
            }

            IDatabase garnet = multiplexer.GetDatabase();
            bool isFirstRequest = await garnet.StringSetAsync(
                $"idempotency:startjob:{idempotencyKey}", 
                "locked", 
                TimeSpan.FromHours(24), 
                When.NotExists);

            if (!isFirstRequest)
            {
                DiagnosticsConfig.IdempotencyBlockedCounter.Add(1,
                    new KeyValuePair<string, object?>("endpoint", "bulk_job"));
                logger.LogInformation("Idempotency lock triggered. Blocked duplicate request for Key [{IdempotencyKey}].", idempotencyKey);
                return Results.Conflict(new { Message = "Duplicate request detected and blocked." });
            }

            string adminId = Ulid.NewUlid().ToString(); 

            try
            {
                // Free-text subject/body come straight from the admin's WYSIWYG editor, so repair
                // any HTML-encoded Scriban delimiters before they are persisted and fanned out.
                EmailJob job = new(
                    adminId,
                    request.JobType,
                    request.TemplateId,
                    request.Subject is null ? null : ScribanContentNormalizer.Normalize(request.Subject),
                    request.BodyTemplate is null ? null : ScribanContentNormalizer.Normalize(request.BodyTemplate),
                    request.Filters);
                await dbContext.EmailJobs.AddAsync(job, ct);
                
                activity?.SetTag("job.id", job.Id.ToString());
                activity?.SetTag("job.type", request.JobType.ToString());
                
                StartBulkEmailJobCommand command = new()
                {
                    JobId = job.Id,
                    TemplateId = request.TemplateId
                };
                
                await publishEndpoint.Publish(command, ct);
                await dbContext.SaveChangesAsync(ct);
                
                DiagnosticsConfig.BulkJobsStartedCounter.Add(1,
                    new KeyValuePair<string, object?>("job_type", request.JobType.ToString()));

                logger.LogInformation("Successfully initiated Bulk Job [{JobId}] of type {JobType}. Handed off to MassTransit.", job.Id, request.JobType);

                return Results.Accepted($"jobs/bulk/{job.Id}", new { JobId = job.Id });
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                logger.LogError(ex, "Catastrophic failure while starting bulk job for Idempotency Key [{IdempotencyKey}].", idempotencyKey);
                throw;
            }
        })
        .WithTags("Bulk Email");
    }
}