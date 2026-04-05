namespace Kariyer.Mail.Api.Features.Account.AccountCreated;

public record AccountCreatedEvent
{
    public string MessageId { get; init; } = string.Empty;
    public string Uid { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string AccountType { get; init; } = string.Empty; 
}