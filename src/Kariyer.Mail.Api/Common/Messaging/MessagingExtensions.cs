using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Features.BulkEmail;
using Kariyer.Mail.Api.Features.DispatchEmail;
using MassTransit;
using Microsoft.Extensions.Options;

namespace Kariyer.Mail.Api.Common.Messaging;

public static class MessagingExtensions
{
    public static IServiceCollection AddMessaging(this IServiceCollection services, string rabbitConn)
    {
        services.AddMassTransit(x =>
        {
            x.AddEntityFrameworkOutbox<MailDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
            });

            x.AddConsumer<ResolverConsumer>();
            x.AddConsumer<DispatchEmailBatchConsumer>();
            
            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(rabbitConn);
                
                cfg.Message<StartBulkEmailJobCommand>(m => m.SetEntityName("MailExchange"));
                cfg.Message<DispatchEmailCommand>(m => m.SetEntityName("MailExchange"));
                
                cfg.ReceiveEndpoint("mail.resolver.queue", e =>
                {
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ConfigureConsumeTopology = false; 
                    e.Bind("MailExchange", s => s.RoutingKey = "bulk.resolve");
                    e.ConfigureConsumer<ResolverConsumer>(context);
                });

                cfg.ReceiveEndpoint("mail.dispatcher.queue", e =>
                {
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    
                    DispatcherSettings dispatcherConfig = context.GetRequiredService<IOptions<DispatcherSettings>>().Value;
                    
                    e.PrefetchCount = dispatcherConfig.PrefetchCount;

                    e.Batch<DispatchEmailCommand>(b =>
                    {
                        b.MessageLimit = dispatcherConfig.BatchSize;
                        b.TimeLimit = TimeSpan.FromSeconds(dispatcherConfig.TimeLimitSeconds);
                        b.ConcurrencyLimit = dispatcherConfig.ConcurrencyLimit;
                    });
                    
                    e.ConfigureConsumer<DispatchEmailBatchConsumer>(context);
                });
                
                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}