namespace Kariyer.Mail.Api.Features.Account.AdminCompanyCompleted;

public record CompanyCompletedEvent
{
    public string MessageId { get; init; } = string.Empty;
    public string CompanyUid { get; init; } = string.Empty;
    public string CompanyName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string AuthorizedName { get; init; } = string.Empty;
    public string AuthorizedSurname { get; init; } = string.Empty;
    public string TaxIdNumber { get; init; } = string.Empty;
    public string TaxOffice { get; init; } = string.Empty;
    public string Province { get; init; } = string.Empty;
    public string Industry { get; init; } = string.Empty;
    public string EmployeeCount { get; init; } = string.Empty;
    public DateTime SubmittedAt { get; init; }
}