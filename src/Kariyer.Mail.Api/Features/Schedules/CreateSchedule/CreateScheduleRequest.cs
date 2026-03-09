using System.Text.Json;
using Kariyer.Mail.Api.Common.Enums;

namespace Kariyer.Mail.Api.Features.Schedules.CreateSchedule;

public sealed record CreateScheduleRequest(
    string Name,
    EmailJobType JobType,
    Ulid? TemplateId,
    string? Subject,
    string? BodyTemplate,
    JsonDocument Filters,
    bool IsRecurring,
    string? CronExpression,
    DateTimeOffset? OneTimeExecuteAt
);