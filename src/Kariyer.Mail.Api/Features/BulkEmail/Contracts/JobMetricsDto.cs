namespace Kariyer.Mail.Api.Features.BulkEmail.Contracts;

public sealed record JobMetricsDto(long TotalResolved, long SuccessfullySent, long FailedToDrop);