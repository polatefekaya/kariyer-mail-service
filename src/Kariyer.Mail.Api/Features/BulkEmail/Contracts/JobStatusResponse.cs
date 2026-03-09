namespace Kariyer.Mail.Api.Features.BulkEmail.Contracts;

public sealed record JobStatusResponseDto(Ulid Id, string Status, string Type, DateTime StartedAt, DateTime? CompletedAt, string? ErrorMessage, JobMetricsDto Metrics);