namespace Kariyer.Mail.Api.Features.Account.AccountApproved;

public record AccountApprovedEvent
{
    public string MessageId { get; init; } = string.Empty;
    public string Uid { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string ApprovedAt { get; init; } = string.Empty;
}