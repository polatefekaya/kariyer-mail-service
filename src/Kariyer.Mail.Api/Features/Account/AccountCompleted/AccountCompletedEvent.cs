namespace Kariyer.Mail.Api.Features.Account.AccountCompleted;

public record AccountCompletedEvent
{
    public string MessageId { get; init; } = string.Empty;
    public string Uid { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
}