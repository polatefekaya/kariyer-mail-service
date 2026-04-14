using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Features.Account.AccountCreated;
using Kariyer.Mail.Api.Features.Account.AccountCompleted;
using Kariyer.Mail.Api.Features.Account.AccountDeleted;
using Kariyer.Mail.Api.Features.Account.AccountDidNotCompleted;
using Kariyer.Mail.Api.Features.Account.AccountFrozen;
using Kariyer.Mail.Api.Features.BulkEmail;
using Kariyer.Mail.Api.Features.DispatchEmail;
using MassTransit;
using Microsoft.Extensions.Options;
using Kariyer.Mail.Api.Features.Account.AdminCompanyCompleted;
using Kariyer.Mail.Api.Features.Account.AccountApproved;
using Kariyer.Mail.Api.Features.Account.AccountRejected;

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
            });

            x.AddConsumer<ResolverConsumer>();
            x.AddConsumer<DispatchEmailConsumer>();
            x.AddConsumer<AccountCreatedConsumer>();
            x.AddConsumer<AccountDidNotCompletedConsumer>();
            x.AddConsumer<AccountCompletedConsumer>();
            x.AddConsumer<AccountFrozenConsumer>();
            x.AddConsumer<AccountDeletedConsumer>();
            x.AddConsumer<AdminCompanyCompletedConsumer>();
            x.AddConsumer<AccountApprovedConsumer>();
            x.AddConsumer<AccountRejectedConsumer>();

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(rabbitConn);

                cfg.Message<StartBulkEmailJobCommand>(m => m.SetEntityName("MailExchange"));
                cfg.Message<DispatchEmailCommand>(m => m.SetEntityName("MailExchange"));
                
                cfg.ReceiveEndpoint("mail.bulk.resolve", e =>
                {
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();
                    
                    e.ConfigureConsumeTopology = false;
                    e.Bind("MailExchange", s => s.RoutingKey = "bulk.resolve");
                    e.ConfigureConsumer<ResolverConsumer>(context);
                });

                cfg.ReceiveEndpoint("mail.bulk.dispatch", e =>
                {
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();

                    DispatcherSettings dispatcherConfig = context.GetRequiredService<IOptions<DispatcherSettings>>().Value;

                    e.PrefetchCount = dispatcherConfig.PrefetchCount;

                    e.Batch<DispatchEmailCommand>(b =>
                    {
                        b.MessageLimit = dispatcherConfig.BatchSize;
                        b.TimeLimit = TimeSpan.FromSeconds(dispatcherConfig.TimeLimitSeconds);
                        b.ConcurrencyLimit = dispatcherConfig.ConcurrencyLimit;
                    });

                    e.ConfigureConsumer<DispatchEmailConsumer>(context);
                });

                cfg.ReceiveEndpoint("mail.account.created", e =>
                {
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();
                    
                    e.ConfigureConsumeTopology = false;
                    e.Bind("identity.account.created", b => b.ExchangeType = "fanout");
                    e.ConfigureConsumer<AccountCreatedConsumer>(context);
                });

                cfg.ReceiveEndpoint("mail.account.not-completed", e =>
                {
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();
                    
                    e.ConfigureConsumeTopology = false; 
                    e.Bind("identity.account.not-completed", b => b.ExchangeType = "fanout");
                    e.ConfigureConsumer<AccountDidNotCompletedConsumer>(context);
                });

                cfg.ReceiveEndpoint("mail.account.completed", e =>
                {
                    e.UseRawJsonSerializer();
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();

                    e.ConfigureConsumeTopology = false;
                    e.Bind("identity.company.completed", b => b.ExchangeType = "fanout");
                    e.ConfigureConsumer<AccountCompletedConsumer>(context);
                });

                cfg.ReceiveEndpoint("mail.admin.company-completed", e =>
                {
                    e.UseRawJsonSerializer();
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();

                    e.ConfigureConsumeTopology = false;
                    e.Bind("identity.company.completed", b => b.ExchangeType = "fanout");
                    e.ConfigureConsumer<AdminCompanyCompletedConsumer>(context);
                });

                cfg.ReceiveEndpoint("mail.account.frozen", e =>
                {
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();
                    
                    e.ConfigureConsumeTopology = false;
                    e.Bind("identity.account.frozen", b => b.ExchangeType = "fanout");
                    e.ConfigureConsumer<AccountFrozenConsumer>(context);
                });

                cfg.ReceiveEndpoint("mail.account.deleted", e =>
                {
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();

                    e.ConfigureConsumeTopology = false;
                    e.Bind("identity.account.deleted", b => b.ExchangeType = "fanout");
                    e.ConfigureConsumer<AccountDeletedConsumer>(context);
                });
                
                cfg.ReceiveEndpoint("mail.account.approved", e =>
                {
                    e.UseRawJsonSerializer();
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();
                    
                
                    e.ConfigureConsumeTopology = false;
                    e.Bind("identity.account.approved", b => b.ExchangeType = "fanout");
                    e.ConfigureConsumer<AccountApprovedConsumer>(context);
                });
            
                cfg.ReceiveEndpoint("mail.account.rejected", e =>
                {
                    e.UseRawJsonSerializer();
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();
                    
                
                    e.ConfigureConsumeTopology = false;
                    e.Bind("identity.account.rejected", b => b.ExchangeType = "fanout");
                    e.ConfigureConsumer<AccountRejectedConsumer>(context);
                });

                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }

    private static void ApplyStandardResilience(this IRabbitMqReceiveEndpointConfigurator endpoint)
    {
        endpoint.UseMessageRetry(r => r.Exponential(
            retryLimit: 3, 
            minInterval: TimeSpan.FromSeconds(5), 
            maxInterval: TimeSpan.FromSeconds(60), 
            intervalDelta: TimeSpan.FromSeconds(5)));

        endpoint.UseCircuitBreaker(cb =>
        {
            cb.TrackingPeriod = TimeSpan.FromMinutes(2);
            cb.TripThreshold = 15; 
            cb.ActiveThreshold = 10;
            cb.ResetInterval = TimeSpan.FromMinutes(1);
        });
    }
}