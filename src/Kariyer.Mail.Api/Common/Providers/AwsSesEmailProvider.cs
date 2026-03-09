using Amazon;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Features.DispatchEmail.Providers;
using Microsoft.Extensions.Options;

namespace Kariyer.Mail.Api.Common.Providers;

internal sealed class AwsSesEmailProvider : IEmailProvider
{
    private readonly IOptionsSnapshot<EmailSettings> _settings;

    public AwsSesEmailProvider(IOptionsSnapshot<EmailSettings> settings)
    {
        _settings = settings;
    }

    public async Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken ct)
    {
        EmailSettings config = _settings.Value;

        using AmazonSimpleEmailServiceV2Client client = new (
            config.AwsAccessKey, 
            config.AwsSecretKey, 
            RegionEndpoint.GetBySystemName(config.AwsRegion));

        SendEmailRequest request = new()
        {
            FromEmailAddress = config.FormattedFromAddress,
            
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

        await client.SendEmailAsync(request, ct);
    }
}