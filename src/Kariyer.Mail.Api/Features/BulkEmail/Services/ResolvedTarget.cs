namespace Kariyer.Mail.Api.Features.BulkEmail.Services;

public sealed record ResolvedTarget(
    string TargetId,
    string Email,
    Dictionary<string, string> Metadata 
);