using System.Text.Json;

namespace Kariyer.Mail.Api.Features.Schedules.UpdateSchedule;

public sealed record UpdateScheduleRequest(
    string Name,
    Ulid? TemplateId,
    string? Subject,
    string? BodyTemplate,
    JsonDocument Filters,
    bool IsRecurring,
    string? CronExpression,
    DateTimeOffset? OneTimeExecuteAt
);