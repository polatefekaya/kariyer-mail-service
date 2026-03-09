using System.Reflection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kariyer.Mail.Api.Common.Web;

public static class EndpointExtensions
{
    public static IServiceCollection AddEndpoints(this IServiceCollection services, Assembly assembly)
    {
        ServiceDescriptor[] serviceDescriptors = assembly
            .DefinedTypes
            .Where(type => type is { IsAbstract: false, IsInterface: false } &&
                           type.IsAssignableTo(typeof(IEndpoint)))
            .Select(type => ServiceDescriptor.Transient(typeof(IEndpoint), type))
            .ToArray();

        services.TryAddEnumerable(serviceDescriptors);
        return services;
    }

    public static IApplicationBuilder MapEndpoints(this WebApplication app)
    {
        IEnumerable<IEndpoint> endpoints = app.Services.GetRequiredService<IEnumerable<IEndpoint>>();

        RouteGroupBuilder apiGroup = app.MapGroup("/api/v1");

        foreach (IEndpoint endpoint in endpoints)
        {
            endpoint.MapEndpoint(apiGroup);
        }

        return app;
    }
}