using Kariyer.Mail.Api.Common.Web;

namespace Kariyer.Mail.Api.Features.Templates.GetPlaceholderSets;

internal sealed class GetPlaceholderSetsEndpoint : IEndpoint
{
    // Static registry: mirrors exactly what each consumer puts into its templateData dict.
    // Update this whenever a consumer's templateData changes.
    private static readonly IReadOnlyList<TemplatePlaceholderSetDto> PlaceholderSets =
    [
        new("AccountCreated", "Triggered when a new user account is registered.",
        [
            new("FullName",     "{{ FullName }}",     "Ahmet Yılmaz"),
            new("AccountType",  "{{ AccountType }}",  "company"),
        ]),

        new("AccountCompleted", "Triggered when a user completes their profile.",
        [
            new("FullName", "{{ FullName }}", "Ahmet Yılmaz"),
        ]),

        new("AccountApproved", "Triggered when a user account is approved.",
        [
            new("FullName",    "{{ FullName }}",    "Ahmet Yılmaz"),
            new("ApprovedAt",  "{{ ApprovedAt }}",  "08.05.2026 12:00"),
        ]),

        new("AccountRejected", "Triggered when a user account application is rejected.",
        [
            new("FullName",     "{{ FullName }}",     "Ahmet Yılmaz"),
            new("Reason",       "{{ Reason }}",       "Eksik evrak"),
            new("RejectedAt",   "{{ RejectedAt }}",   "08.05.2026 12:00"),
        ]),

        new("AccountFrozen", "Triggered when a user account is frozen.",
        [
            new("FullName", "{{ FullName }}", "Ahmet Yılmaz"),
            new("Reason",   "{{ Reason }}",   "Şüpheli aktivite"),
        ]),

        new("AccountDeleted", "Triggered when a user account is deleted.",
        [
            new("FullName", "{{ FullName }}", "Ahmet Yılmaz"),
        ]),

        new("AccountDidNotCompleted", "Triggered as a reminder when a user has not completed their profile.",
        [
            new("FullName",       "{{ FullName }}",       "Ahmet Yılmaz"),
            new("AccountType",    "{{ AccountType }}",    "company"),
            new("ReminderStep",   "{{ ReminderStep }}",   "2"),
        ]),

        new("AdminCompanyCompleted", "Admin notification sent when a company completes their profile.",
        [
            new("CompanyName",      "{{ CompanyName }}",      "Kariyer Yazılım A.Ş."),
            new("Email",            "{{ Email }}",            "info@kariyer.net"),
            new("Phone",            "{{ Phone }}",            "+90 212 000 0000"),
            new("AuthorizedPerson", "{{ AuthorizedPerson }}", "Ahmet Yılmaz"),
            new("TaxIdNumber",      "{{ TaxIdNumber }}",      "1234567890"),
            new("TaxOffice",        "{{ TaxOffice }}",        "Kadıköy"),
            new("Province",         "{{ Province }}",         "İstanbul"),
            new("Industry",         "{{ Industry }}",         "Yazılım"),
            new("EmployeeCount",    "{{ EmployeeCount }}",    "50-100"),
            new("CompanyUid",       "{{ CompanyUid }}",       "01ARZ3NDEKTSV4RRFFQ69G5FAV"),
            new("SubmittedAt",      "{{ SubmittedAt }}",      "08.05.2026 12:00"),
        ]),

        new("BulkEmail", "Used in manually triggered bulk email jobs. Available metadata depends on the job's filter/payload.",
        [
            new("Email", "{{ Email }}", "user@example.com"),
        ]),
    ];

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("templates/placeholder-sets", () => Results.Ok(PlaceholderSets))
            .WithTags("Templates");
    }
}
