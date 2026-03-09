namespace Kariyer.Mail.Api.Features.BulkEmail;

public record StartBulkEmailJobCommand
{
    public Ulid JobId { get; init; }
    public Ulid? TemplateId { get; init; }
}