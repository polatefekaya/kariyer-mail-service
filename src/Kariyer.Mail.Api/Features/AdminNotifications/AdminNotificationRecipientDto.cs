namespace Kariyer.Mail.Api.Features.AdminNotifications;

public sealed record AdminNotificationRecipientDto(Ulid Id, string Email, string? Label, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt);
