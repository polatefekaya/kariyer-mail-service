using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Features.Account.AccountCreated;
using Kariyer.Mail.Api.Features.Account.AccountCompleted;
using Kariyer.Mail.Api.Features.Account.AccountDeleted;
using Kariyer.Mail.Api.Features.Account.AccountDidNotCompleted;
using Kariyer.Mail.Api.Features.Account.AccountDeletionCancelled;
using Kariyer.Mail.Api.Features.Account.AccountFrozen;
using Kariyer.Mail.Api.Features.BulkEmail;
using Kariyer.Mail.Api.Features.DispatchEmail;
using Kariyer.Mail.Api.Features.JobAlert;
using Kariyer.Mail.Api.Features.Recruiting.ApplicationStageChanged;
using Kariyer.Mail.Api.Features.Recruiting.ApplicationSubmitted;
using Kariyer.Mail.Api.Features.Recruiting.ApplicationWithdrawn;
using Kariyer.Mail.Api.Features.Recruiting.InterviewAnswered;
using Kariyer.Mail.Api.Features.Recruiting.InterviewCancelled;
using Kariyer.Mail.Api.Features.Recruiting.InterviewInvited;
using Kariyer.Mail.Api.Features.Recruiting.InterviewRescheduled;
using MassTransit;
using Microsoft.Extensions.Options;
using Kariyer.Mail.Api.Features.Account.AdminCompanyCompleted;
using Kariyer.Mail.Api.Features.Account.AccountApproved;
using Kariyer.Mail.Api.Features.Account.AccountRejected;
using Kariyer.Mail.Api.Features.Account.AccountEmailChanged;
using Kariyer.Mail.Api.Features.Account.AccountPhoneChanged;
using Kariyer.Mail.Api.Features.Account.AccountUsernameChanged;

namespace Kariyer.Mail.Api.Common.Messaging;

public static class MessagingExtensions
{
    public static IServiceCollection AddMessaging(this IServiceCollection services, string rabbitConn)
    {
        services.AddSingleton<MailConsumeObserver>();

        services.AddMassTransit(x =>
        {
            x.AddEntityFrameworkOutbox<MailDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
            });

            x.AddConsumer<ResolverConsumer>();
            x.AddConsumer<DispatchEmailConsumer>();
            x.AddConsumer<AccountCreatedConsumer>();
            x.AddConsumer<AccountDidNotCompletedConsumer>();
            x.AddConsumer<AccountCompletedConsumer>();
            x.AddConsumer<AccountFrozenConsumer>();
            x.AddConsumer<AccountDeletedConsumer>();
            x.AddConsumer<AccountDeletionCancelledConsumer>();
            x.AddConsumer<AdminCompanyCompletedConsumer>();
            x.AddConsumer<AccountApprovedConsumer>();
            x.AddConsumer<AccountRejectedConsumer>();
            x.AddConsumer<AccountEmailChangedConsumer>();
            x.AddConsumer<AccountPhoneChangedConsumer>();
            x.AddConsumer<AccountUsernameChangedConsumer>();
            x.AddConsumer<JobAlertReadyConsumer>();
            x.AddConsumer<InterviewInvitedConsumer>();
            x.AddConsumer<InterviewRescheduledConsumer>();
            x.AddConsumer<InterviewCancelledConsumer>();
            x.AddConsumer<InterviewAnsweredConsumer>();
            x.AddConsumer<ApplicationSubmittedConsumer>();
            x.AddConsumer<ApplicationWithdrawnConsumer>();
            x.AddConsumer<ApplicationStageChangedConsumer>();

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(new Uri(rabbitConn));
                cfg.UseRawJsonDeserializer(RawSerializerOptions.AnyMessageType);
                cfg.ConnectConsumeObserver(context.GetRequiredService<MailConsumeObserver>());

                cfg.ReceiveEndpoint("mail.bulk.resolve", e =>
                {
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();
                    e.ConfigureConsumer<ResolverConsumer>(context);
                });

                cfg.ReceiveEndpoint("mail.bulk.dispatch", e =>
                {
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();

                    DispatcherSettings dispatcherConfig = context.GetRequiredService<IOptions<DispatcherSettings>>().Value;

                    e.PrefetchCount = dispatcherConfig.PrefetchCount;
                    e.ConcurrentMessageLimit = dispatcherConfig.ConcurrencyLimit;

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
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();

                    e.ConfigureConsumeTopology = false;
                    e.Bind("identity.company.completed", b => b.ExchangeType = "fanout");
                    e.ConfigureConsumer<AccountCompletedConsumer>(context);
                });

                // İş Uyarıları. Bound to job.alert.ready, which kariyer_zamani_backend declares
                // as a durable fanout and publishes to once per employee per digest run.
                //
                // Its own queue rather than sharing one: this is the only recurring,
                // consent-gated mail the service sends, and a backlog of digests must not
                // sit behind — or ahead of — account lifecycle mail somebody is waiting on.
                cfg.ReceiveEndpoint("mail.job-alert.ready", e =>
                {
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();

                    e.ConfigureConsumeTopology = false;
                    e.Bind("job.alert.ready", b => b.ExchangeType = "fanout");
                    e.ConfigureConsumer<JobAlertReadyConsumer>(context);
                });

                // Mülakat bildirimleri. kariyer-recruiting-service publishes these through its
                // transactional outbox, so a committed invitation cannot fail to be announced.
                //
                // One queue each rather than one shared queue: a cancellation is the most
                // time-critical mail this service sends — the candidate may be about to travel —
                // and it must not wait behind a backlog of invitations.
                cfg.ReceiveEndpoint("mail.recruiting.interview-invited", e =>
                {
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();

                    e.ConfigureConsumeTopology = false;
                    e.Bind("recruiting.interview.invited", b => b.ExchangeType = "fanout");
                    e.ConfigureConsumer<InterviewInvitedConsumer>(context);
                });

                cfg.ReceiveEndpoint("mail.recruiting.interview-rescheduled", e =>
                {
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();

                    e.ConfigureConsumeTopology = false;
                    e.Bind("recruiting.interview.rescheduled", b => b.ExchangeType = "fanout");
                    e.ConfigureConsumer<InterviewRescheduledConsumer>(context);
                });

                cfg.ReceiveEndpoint("mail.recruiting.interview-cancelled", e =>
                {
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();

                    e.ConfigureConsumeTopology = false;
                    e.Bind("recruiting.interview.cancelled", b => b.ExchangeType = "fanout");
                    e.ConfigureConsumer<InterviewCancelledConsumer>(context);
                });

                cfg.ReceiveEndpoint("mail.recruiting.interview-answered", e =>
                {
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();

                    e.ConfigureConsumeTopology = false;
                    e.Bind("recruiting.interview.answered", b => b.ExchangeType = "fanout");
                    e.ConfigureConsumer<InterviewAnsweredConsumer>(context);
                });

                // Başvuru bildirimleri. Submitted and withdrawn are published by
                // kariyer_zamani_backend, where applications are created and withdrawn; the
                // stage change by kariyer-recruiting-service. Each on its own queue for the same
                // reason as the interview mail: a burst of new applications on a popular posting
                // must not delay a candidate's decision mail.
                cfg.ReceiveEndpoint("mail.recruiting.application-submitted", e =>
                {
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();

                    e.ConfigureConsumeTopology = false;
                    e.Bind("recruiting.application.submitted", b => b.ExchangeType = "fanout");
                    e.ConfigureConsumer<ApplicationSubmittedConsumer>(context);
                });

                cfg.ReceiveEndpoint("mail.recruiting.application-withdrawn", e =>
                {
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();

                    e.ConfigureConsumeTopology = false;
                    e.Bind("recruiting.application.withdrawn", b => b.ExchangeType = "fanout");
                    e.ConfigureConsumer<ApplicationWithdrawnConsumer>(context);
                });

                // Every stage move, silent ones included — a silent move is what cancels a
                // decision mail still being held. Nothing is sent from this queue directly;
                // PendingStageMailDispatchJob sends once the hold has passed.
                cfg.ReceiveEndpoint("mail.recruiting.application-stage-changed", e =>
                {
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();

                    e.ConfigureConsumeTopology = false;
                    e.Bind("recruiting.application.stage_changed", b => b.ExchangeType = "fanout");
                    e.ConfigureConsumer<ApplicationStageChangedConsumer>(context);
                });

                cfg.ReceiveEndpoint("mail.admin.company-completed", e =>
                {
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

                cfg.ReceiveEndpoint("mail.account.deletion-cancelled", e =>
                {
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();

                    e.ConfigureConsumeTopology = false;
                    e.Bind("identity.account.deletion-cancelled", b => b.ExchangeType = "fanout");
                    e.ConfigureConsumer<AccountDeletionCancelledConsumer>(context);
                });

                cfg.ReceiveEndpoint("mail.account.approved", e =>
                {
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();

                    e.ConfigureConsumeTopology = false;
                    e.Bind("identity.account.approved", b => b.ExchangeType = "fanout");
                    e.ConfigureConsumer<AccountApprovedConsumer>(context);
                });

                cfg.ReceiveEndpoint("mail.account.rejected", e =>
                {
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();

                    e.ConfigureConsumeTopology = false;
                    e.Bind("identity.account.rejected", b => b.ExchangeType = "fanout");
                    e.ConfigureConsumer<AccountRejectedConsumer>(context);
                });

                cfg.ReceiveEndpoint("mail.account.email-changed", e =>
                {
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();

                    e.ConfigureConsumeTopology = false;
                    e.Bind("identity.account.email-changed", b => b.ExchangeType = "fanout");
                    e.ConfigureConsumer<AccountEmailChangedConsumer>(context);
                });

                cfg.ReceiveEndpoint("mail.account.phone-changed", e =>
                {
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();

                    e.ConfigureConsumeTopology = false;
                    e.Bind("identity.account.phone-changed", b => b.ExchangeType = "fanout");
                    e.ConfigureConsumer<AccountPhoneChangedConsumer>(context);
                });

                cfg.ReceiveEndpoint("mail.account.username-changed", e =>
                {
                    e.UseEntityFrameworkOutbox<MailDbContext>(context);
                    e.ApplyStandardResilience();

                    e.ConfigureConsumeTopology = false;
                    e.Bind("identity.account.username-changed", b => b.ExchangeType = "fanout");
                    e.ConfigureConsumer<AccountUsernameChangedConsumer>(context);
                });

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