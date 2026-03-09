using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Features.DispatchEmail.Providers;
using Microsoft.Extensions.Options;

namespace Kariyer.Mail.Api.Common.Providers;

internal sealed class EmailProviderFactory : IEmailProviderFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsSnapshot<EmailSettings> _settings;

    public EmailProviderFactory(IServiceProvider serviceProvider, IOptionsSnapshot<EmailSettings> settings)
    {
        _serviceProvider = serviceProvider;
        _settings = settings;
    }

    public IEmailProvider GetActiveProvider()
    {
        string? providerKey = _settings.Value.ActiveProvider;

        if (string.IsNullOrWhiteSpace(providerKey))
        {
            throw new InvalidOperationException("CRITICAL FAIL: 'ActiveProvider' is missing from configuration. Email dispatch is halted.");
        }

        IEmailProvider? provider = _serviceProvider.GetKeyedService<IEmailProvider>(providerKey);

        if (provider == null)
        {
            throw new InvalidOperationException($"CRITICAL FAIL: ActiveProvider '{providerKey}' is not registered in the DI container.");
        }

        return provider;
    }
}