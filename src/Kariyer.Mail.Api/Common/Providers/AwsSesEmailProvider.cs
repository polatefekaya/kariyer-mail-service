using System.Diagnostics;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Features.DispatchEmail.Providers;
using Microsoft.Extensions.Options;

namespace Kariyer.Mail.Api.Common.Providers;

internal sealed class AwsSesEmailProvider : IEmailProvider
{
    private readonly IAmazonSimpleEmailServiceV2 _sesClient;
    private readonly IOptionsSnapshot<EmailSettings> _settings;
    private readonly ILogger<AwsSesEmailProvider> _logger;

    public AwsSesEmailProvider(
        IAmazonSimpleEmailServiceV2 sesClient,
        IOptionsSnapshot<EmailSettings> settings,
        ILogger<AwsSesEmailProvider> logger)
    {
        _sesClient = sesClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken ct)
    {
        string fromAddress = _settings.Value.FormattedFromAddress;

        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity(
            "AwsSesProvider.Send", ActivityKind.Client);
        activity?.SetTag("mail.provider", "aws_ses");
        activity?.SetTag("mail.recipient_type", "single");
        activity?.SetTag("mail.from", fromAddress);

        long startTs = Stopwatch.GetTimestamp();

        try
        {
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

            SendEmailResponse response = await _sesClient.SendEmailAsync(request, ct);

            activity?.SetTag("mail.ses_message_id", response.MessageId);
            activity?.SetTag("mail.status", "sent");
            activity?.SetStatus(ActivityStatusCode.Ok);

            _logger.LogDebug("AWS SES accepted message [{SesMessageId}] for delivery.", response.MessageId);
        }
        catch (Exception ex)
        {
            activity?.SetTag("mail.status", "failed");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            _logger.LogError(ex, "AWS SES rejected send request: {ErrorMessage}", ex.Message);
            throw;
        }
        finally
        {
            DiagnosticsConfig.EmailSendDuration.Record(
                Stopwatch.GetElapsedTime(startTs).TotalMilliseconds,
                new KeyValuePair<string, object?>("provider", "aws_ses"));
        }
    }
}
