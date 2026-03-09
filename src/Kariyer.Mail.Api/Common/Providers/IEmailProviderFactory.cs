using Kariyer.Mail.Api.Features.DispatchEmail.Providers;

namespace Kariyer.Mail.Api.Common.Providers;

public interface IEmailProviderFactory
{
    IEmailProvider GetActiveProvider();
}