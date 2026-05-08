namespace Kariyer.Mail.Api.Features.AdminNotifications;

internal interface IAdminNotificationService
{
    Task<IReadOnlyList<AdminNotificationRecipientDto>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetActiveEmailsAsync(CancellationToken ct = default);
    Task InvalidateCacheAsync();
}
