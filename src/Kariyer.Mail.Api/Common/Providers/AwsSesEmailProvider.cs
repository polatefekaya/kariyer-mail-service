using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Features.DispatchEmail.Providers;
using Microsoft.Extensions.Options;

namespace Kariyer.Mail.Api.Common.Providers;

internal sealed class AwsSesEmailProvider : IEmailProvider
{
    private readonly IAmazonSimpleEmailServiceV2 _sesClient;
    private readonly IOptionsSnapshot<EmailSettings> _settings;

    public AwsSesEmailProvider(
        IAmazonSimpleEmailServiceV2 sesClient, 
        IOptionsSnapshot<EmailSettings> settings)
    {
        _sesClient = sesClient;
        _settings = settings;
    }

    public async Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken ct)
    {
        string fromAddress = _settings.Value.FormattedFromAddress;

        SendEmailRequest request = new()
        {
            FromEmailAddress = fromAddress,
            Destination = new Destination { ToAddresses = new List<string> { to } },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = subject },
                    Body = new Body { Html = new Content { Data = htmlBody } }
                }
            }
        };
        await _sesClient.SendEmailAsync(request, ct);
    }
}