using System;

namespace Kariyer.Mail.Application;

public static class DependencyInection
{
    public static IServiceCollection InjectApplicationLayer(this IServiceCollection services)
    {
        return services;
    }
}
