using System;

namespace Kariyer.Mail.Infrastructure;

public static class DependencyInection
{
    public static IServiceCollection InjectInfrastructureLayer(this IServiceCollection services)
    {
        return services;
    }
}
