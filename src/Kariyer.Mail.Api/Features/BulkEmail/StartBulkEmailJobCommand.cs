namespace Kariyer.Mail.Api.Features.BulkEmail;

public sealed record StartBulkEmailJobCommand(
    Ulid JobId
);