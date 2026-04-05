namespace Kariyer.Mail.Api.Features.Account.AccountDidNotCompleted;

public record AccountDidNotCompletedEvent
{
    public string MessageId { get; init; } = string.Empty;
    public string Uid { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string AccountType { get; init; } = string.Empty; // "company" or "employee"
    public int ReminderStep { get; init; } // 1 for 24h, 2 for 3 days, etc.
}