using System.Text.Json;

namespace Kariyer.Mail.Api.Features.Schedules.GetSchedule.Contracts;

public sealed record ScheduleDetailDto(
    Ulid Id, string Name, string JobType, Ulid? TemplateId, 
    string? Subject, string? BodyTemplate, JsonDocument Filters, 
    bool IsRecurring, string? CronExpression, DateTimeOffset? OneTimeExecuteAt, 
    bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt);