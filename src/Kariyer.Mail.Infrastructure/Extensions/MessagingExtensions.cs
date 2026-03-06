using System;
using System.Reflection;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace Kariyer.Mail.Infrastructure.Extensions;

public static class MessagingExtensions
{
    public static IServiceCollection AddWildroseMessaging(this IServiceCollection services, string rabbitConnectionString)
    {
        services.AddMassTransit(configure =>
        {
            configure.SetKebabCaseEndpointNameFormatter();

            configure.AddConsumers(Assembly.GetExecutingAssembly());

            configure.UsingRabbitMq((context, rabbitCfg) =>
            {
                rabbitCfg.Host(rabbitConnectionString);

                rabbitCfg.ConfigureEndpoints(context); 
            });
        });

        return services;
    }
}
