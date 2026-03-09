using System.Net.Http.Headers;
using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Features.DispatchEmail.Providers;
using Microsoft.Extensions.Options;

namespace Kariyer.Mail.Api.Common.Providers;

internal sealed class SendGridEmailProvider : IEmailProvider
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsSnapshot<EmailSettings> _settings;

    public SendGridEmailProvider(HttpClient httpClient, IOptionsSnapshot<EmailSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public async Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken ct)
    {
        EmailSettings config = _settings.Value;
        
        HttpRequestMessage request = new (HttpMethod.Post, "v3/mail/send");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.SendGridToken);
        
        request.Content = JsonContent.Create(new
        {
            personalizations = new[] { new { to = new[] { new { email = to } } } },
            from = new { email = config.FormattedFromAddress },
            subject = subject,
            content = new[] { new { type = "text/html", value = htmlBody } }
        });

        HttpResponseMessage response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}