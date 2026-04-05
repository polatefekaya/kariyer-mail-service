namespace Kariyer.Mail.Api.Features.Account.AccountDeleted;

public record AccountDeletedEvent
{
    public string MessageId { get; init; } = string.Empty;
    public string Uid { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
}