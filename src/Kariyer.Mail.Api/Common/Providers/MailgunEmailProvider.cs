using System.Net.Http.Headers;
using System.Text;
using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Features.DispatchEmail.Providers;
using Microsoft.Extensions.Options;

namespace Kariyer.Mail.Api.Common.Providers;

internal sealed class MailgunEmailProvider : IEmailProvider
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsSnapshot<EmailSettings> _settings;

    public MailgunEmailProvider(HttpClient httpClient, IOptionsSnapshot<EmailSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public async Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken ct)
    {
        EmailSettings config = _settings.Value;
        
        HttpRequestMessage request = new (HttpMethod.Post, $"{config.MailgunDomain}/messages");
        
        string authHeader = Convert.ToBase64String(Encoding.ASCII.GetBytes($"api:{config.MailgunApiKey}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authHeader);
        
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "from", config.FormattedFromAddress },
            { "to", to },
            { "subject", subject },
            { "html", htmlBody }
        });

        HttpResponseMessage response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}