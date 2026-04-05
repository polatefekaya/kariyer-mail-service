namespace Kariyer.Mail.Api.Features.Account.AccountFrozen;

public record AccountFrozenEvent
{
    public string MessageId { get; init; } = string.Empty;
    public string Uid { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}