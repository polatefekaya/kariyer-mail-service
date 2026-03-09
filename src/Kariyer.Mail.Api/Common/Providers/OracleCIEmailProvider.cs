using System.Threading;
using System.Threading.Tasks;
using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Features.DispatchEmail.Providers;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace Kariyer.Mail.Api.Common.Providers;

internal sealed class OracleCIEmailProvider : IEmailProvider
{
    private readonly IOptionsSnapshot<EmailSettings> _settings;

    public OracleCIEmailProvider(IOptionsSnapshot<EmailSettings> settings)
    {
        _settings = settings;
    }

    public async Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken ct)
    {
        EmailSettings config = _settings.Value;

        MimeMessage message = new ();
        message.From.Add(new MailboxAddress(config.FromName, config.FromAddress));
        message.To.Add(new MailboxAddress(string.Empty, to));
        message.Subject = subject;
        message.Body = new TextPart(TextFormat.Html) { Text = htmlBody };

        using SmtpClient client = new();
        
        try
        {
            await client.ConnectAsync(config.SmtpHost, 587, SecureSocketOptions.StartTls, ct);
            await client.AuthenticateAsync(config.SmtpUser, config.SmtpPass, ct);
            await client.SendAsync(message, ct);
        }
        finally
        {
            await client.DisconnectAsync(true, ct);
        }
    }
}