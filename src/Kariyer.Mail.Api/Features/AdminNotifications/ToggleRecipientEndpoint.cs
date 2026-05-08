using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Web;
using Microsoft.EntityFrameworkCore;

namespace Kariyer.Mail.Api.Features.AdminNotifications;

internal sealed class ToggleRecipientEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("notification-recipients/{id:ulid}/toggle", async (
            Ulid id,
            MailDbContext dbContext,
            IAdminNotificationService service,
            ILogger<ToggleRecipientEndpoint> logger,
            CancellationToken ct) =>
        {
            AdminNotificationRecipient? recipient = await dbContext.AdminNotificationRecipients
                .FirstOrDefaultAsync(r => r.Id == id, ct);

            if (recipient == null)
                return Results.NotFound(new { Message = "Recipient not found." });

            recipient.Toggle();
            await dbContext.SaveChangesAsync(ct);

            await service.InvalidateCacheAsync();

            logger.LogInformation("Admin notification recipient [{RecipientId}] toggled to IsActive={IsActive}.", id, recipient.IsActive);
            return Results.Ok(new { IsActive = recipient.IsActive });
        })
        .WithTags("AdminNotifications");
    }
}
