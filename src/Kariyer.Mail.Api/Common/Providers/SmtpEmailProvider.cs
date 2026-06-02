using System.Diagnostics;
using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Features.DispatchEmail.Providers;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace Kariyer.Mail.Api.Common.Providers;

internal sealed class SmtpEmailProvider : IEmailProvider
{
    private readonly IOptionsSnapshot<EmailSettings> _settings;

    public SmtpEmailProvider(IOptionsSnapshot<EmailSettings> settings) => _settings = settings;

    public async Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken ct)
    {
        EmailSettings config = _settings.Value;

        MimeMessage message = new();
        message.From.Add(new MailboxAddress(config.FromName, config.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart(TextFormat.Html) { Text = htmlBody };

        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("SmtpProvider.Send");
        activity?.SetTag("mail.provider", "smtp");
        activity?.SetTag("mail.smtp_host", config.SmtpHost);
        activity?.SetTag("mail.recipient_type", "single");

        long startTs = Stopwatch.GetTimestamp();

        using SmtpClient client = new();
        try
        {
            await client.ConnectAsync(config.SmtpHost, config.SmtpPort, SecureSocketOptions.StartTls, ct);
            await client.AuthenticateAsync(config.SmtpUser, config.SmtpPass, ct);
            await client.SendAsync(message, ct);

            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            throw;
        }
        finally
        {
            double elapsedMs = Stopwatch.GetElapsedTime(startTs).TotalMilliseconds;
            DiagnosticsConfig.EmailSendDuration.Record(elapsedMs,
                new KeyValuePair<string, object?>("provider", "smtp"));

            await client.DisconnectAsync(true, ct);
        }
    }
}
