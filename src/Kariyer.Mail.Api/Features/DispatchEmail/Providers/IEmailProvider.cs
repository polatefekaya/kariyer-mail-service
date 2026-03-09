namespace Kariyer.Mail.Api.Features.DispatchEmail.Providers;

public interface IEmailProvider
{
    public Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken ct);
}