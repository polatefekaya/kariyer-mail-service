namespace Kariyer.Mail.Api.Features.Schedules.GetAllSchedules.Contracts;

public sealed record ScheduleSummaryDto(
    Ulid Id, string Name, string JobType, bool IsRecurring,
    string? CronExpression, DateTimeOffset? OneTimeExecuteAt,
    bool IsActive, DateTime CreatedAt);
