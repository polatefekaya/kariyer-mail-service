using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
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
            CancellationToken ct) =>
        {
            string? idempotencyKey = req.Headers["X-Idempotency-Key"];
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
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
                return Results.Conflict(new { Message = "Duplicate request detected and blocked." });
            }

            string adminId = Ulid.NewUlid().ToString(); 

            EmailJob job = new (adminId, request.JobType, request.TemplateId, request.Subject, request.BodyTemplate, request.Filters);
            await dbContext.EmailJobs.AddAsync(job, ct);
            
            StartBulkEmailJobCommand command = new()
            {
                JobId = job.Id,
                TemplateId = request.TemplateId
            };
            
            await publishEndpoint.Publish(command, ct);
            await dbContext.SaveChangesAsync(ct);
            return Results.Accepted($"jobs/bulk/{job.Id}", new { JobId = job.Id });
        })
        .WithTags("Bulk Email");
    }
}