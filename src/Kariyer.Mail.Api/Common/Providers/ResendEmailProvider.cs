using System.Net.Http.Headers;
using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Features.DispatchEmail.Providers;
using Microsoft.Extensions.Options;

namespace Kariyer.Mail.Api.Common.Providers;

internal sealed class ResendEmailProvider : IEmailProvider
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsSnapshot<EmailSettings> _settings;

    public ResendEmailProvider(HttpClient httpClient, IOptionsSnapshot<EmailSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public async Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken ct)
    {
        EmailSettings config = _settings.Value;
        
        HttpRequestMessage request = new (HttpMethod.Post, "emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ResendToken);
        
        request.Content = JsonContent.Create(new
        {
            from = config.FormattedFromAddress,
            to = new[] { to },
            subject = subject,
            html = htmlBody
        });

        HttpResponseMessage response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode(); 
    }
}