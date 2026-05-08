using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Web;
using Microsoft.EntityFrameworkCore;

namespace Kariyer.Mail.Api.Features.AdminNotifications;

internal sealed class DeleteRecipientEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("notification-recipients/{id:ulid}", async (
            Ulid id,
            MailDbContext dbContext,
            IAdminNotificationService service,
            ILogger<DeleteRecipientEndpoint> logger,
            CancellationToken ct) =>
        {
            int deleted = await dbContext.AdminNotificationRecipients
                .Where(r => r.Id == id)
                .ExecuteDeleteAsync(ct);

            if (deleted == 0)
                return Results.NotFound(new { Message = "Recipient not found." });

            await service.InvalidateCacheAsync();

            logger.LogInformation("Admin notification recipient [{RecipientId}] deleted.", id);
            return Results.NoContent();
        })
        .WithTags("AdminNotifications");
    }
}
