using Kariyer.Mail.Api.Common.Web;

namespace Kariyer.Mail.Api.Features.AdminNotifications;

internal sealed class GetRecipientsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("notification-recipients", async (
            IAdminNotificationService service,
            CancellationToken ct) =>
        {
            IReadOnlyList<AdminNotificationRecipientDto> recipients = await service.GetAllAsync(ct);
            return Results.Ok(recipients);
        })
        .WithTags("AdminNotifications");
    }
}
