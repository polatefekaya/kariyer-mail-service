using Kariyer.Mail.Api.Common.Enums;
namespace Kariyer.Mail.Api.Features.DispatchEmail.Contracts;

internal readonly record struct TargetDispatchResult(
    Ulid TargetId, 
    Ulid? JobId, 
    TargetStatus Status, 
    string? Error
);