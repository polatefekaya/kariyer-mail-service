using System.Text.Json;
using Kariyer.Mail.Api.Common.Enums;

namespace Kariyer.Mail.Api.Features.BulkEmail.Contracts;

public sealed record CreateBulkEmailRequest(
    EmailJobType JobType,
    Ulid? TemplateId, 
    string? Subject,
    string? BodyTemplate,
    JsonDocument Filters
);