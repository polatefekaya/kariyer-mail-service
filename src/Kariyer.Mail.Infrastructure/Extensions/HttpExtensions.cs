using System;
using Microsoft.Extensions.DependencyInjection;

namespace Kariyer.Mail.Infrastructure.Extensions;

public static class HttpExtensions
{
    public static IServiceCollection AddEmailProviderClient(this IServiceCollection services, string baseUrl, string token)
    {
        services.AddHttpClient("EmailProvider", client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        })
        .AddStandardResilienceHandler();

        return services;
    }
}
