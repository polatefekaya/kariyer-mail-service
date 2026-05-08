using System.ComponentModel.DataAnnotations;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Web;
using Microsoft.EntityFrameworkCore;

namespace Kariyer.Mail.Api.Features.AdminNotifications;

public sealed record AddRecipientRequest(
    [property: Required, EmailAddress] string Email,
    string? Label);

internal sealed class AddRecipientEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("notification-recipients", async (
            AddRecipientRequest request,
            MailDbContext dbContext,
            IAdminNotificationService service,
            ILogger<AddRecipientEndpoint> logger,
            CancellationToken ct) =>
        {
            string email = request.Email.Trim().ToLowerInvariant();

            bool exists = await dbContext.AdminNotificationRecipients
                .AnyAsync(r => r.Email == email, ct);

            if (exists)
                return Results.Conflict(new { Message = $"'{email}' is already a notification recipient." });

            AdminNotificationRecipient recipient = new(email, request.Label?.Trim());
            dbContext.AdminNotificationRecipients.Add(recipient);
            await dbContext.SaveChangesAsync(ct);

            await service.InvalidateCacheAsync();

            logger.LogInformation("Admin notification recipient [{Email}] added.", email);
            return Results.Ok(new { RecipientId = recipient.Id });
        })
        .WithTags("AdminNotifications");
    }
}
