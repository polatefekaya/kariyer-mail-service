using System.Diagnostics;
using System.Text.Json;
using Hangfire;
using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Common.Enums;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Features.Templates;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kariyer.Mail.Api.Features.Recruiting.ApplicationStageChanged;

/// <summary>
/// Sends the held decision mails whose hold has passed. Runs every minute from Hangfire.
///
/// Each mail is its own transaction: the target, its dispatch (through the bus outbox) and the
/// row's move to Dispatched commit together. If a newer move touched the row in between, the
/// version check fails, nothing is written, and the newer move stands.
/// </summary>
public sealed class PendingStageMailDispatchJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<RecruitingMailSettings> _settings;
    private readonly IOptions<EmailTemplateSettings> _templateSettings;
    private readonly ILogger<PendingStageMailDispatchJob> _logger;

    public PendingStageMailDispatchJob(
        IServiceScopeFactory scopeFactory,
        IOptions<RecruitingMailSettings> settings,
        IOptions<EmailTemplateSettings> templateSettings,
        ILogger<PendingStageMailDispatchJob> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _templateSettings = templateSettings;
        _logger = logger;
    }

    // A run that outlasts the minute must not overlap the next one and send the same row twice.
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("PendingStageMailDispatchJob");

        if (!_settings.Value.StageMailsEnabled)
        {
            // Anything held from before the switch was turned off is dropped rather than left
            // waiting: switching it back on must not flush stale decisions to candidates.
            await CancelHeldAsync(ct);
            return;
        }

        List<string> due;

        using (IServiceScope scope = _scopeFactory.CreateScope())
        {
            MailDbContext dbContext = scope.ServiceProvider.GetRequiredService<MailDbContext>();
            DateTime now = DateTime.UtcNow;

            due = await dbContext.PendingStageMails
                .Where(m => m.Status == PendingStageMailStatus.Pending && m.DueAt <= now)
                .OrderBy(m => m.DueAt)
                .Select(m => m.ApplicationUid)
                .Take(_settings.Value.DispatchBatchSize)
                .ToListAsync(ct);
        }

        activity?.SetTag("stage_mail.due", due.Count);

        foreach (string applicationUid in due)
        {
            ct.ThrowIfCancellationRequested();
            await DispatchAsync(applicationUid, ct);
        }
    }

    private async Task CancelHeldAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        MailDbContext dbContext = scope.ServiceProvider.GetRequiredService<MailDbContext>();

        List<PendingStageMail> held = await dbContext.PendingStageMails
            .Where(m => m.Status == PendingStageMailStatus.Pending)
            .Take(_settings.Value.DispatchBatchSize)
            .ToListAsync(ct);

        if (held.Count == 0)
        {
            return;
        }

        foreach (PendingStageMail mail in held)
        {
            mail.Cancel("Stage mails are disabled (RecruitingMail:StageMailsEnabled).");
        }

        try
        {
            await dbContext.SaveChangesAsync(ct);
            _logger.LogInformation("Cancelled {Count} held decision mail(s); stage mails are disabled.", held.Count);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A move touched one of them meanwhile; the next run picks up whatever is left.
        }
    }

    private async Task DispatchAsync(string applicationUid, CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        MailDbContext dbContext = scope.ServiceProvider.GetRequiredService<MailDbContext>();
        IPublishEndpoint publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        ITemplateResolutionService templateService = scope.ServiceProvider.GetRequiredService<ITemplateResolutionService>();

        PendingStageMail? mail = await dbContext.PendingStageMails
            .FirstOrDefaultAsync(m => m.ApplicationUid == applicationUid, ct);

        if (mail is not { Status: PendingStageMailStatus.Pending } || mail.DueAt > DateTime.UtcNow)
        {
            return;
        }

        string slug = TemplateContextRegistry.TryGetBySettingsKey(mail.SettingsKey, out TemplateContextDefinition slot)
            ? slot.SlugAccessor!(_templateSettings.Value)
            : string.Empty;

        Dictionary<string, string> templateData =
            JsonSerializer.Deserialize<Dictionary<string, string>>(mail.TemplateData ?? "{}") ?? new();

        try
        {
            mail.MarkDispatched();

            await RecruitingMail.QueueAsync(
                dbContext, publishEndpoint, templateService,
                slug, mail.SettingsKey ?? "(unknown slot)", mail.RecipientUserId, mail.RecipientEmail!,
                templateData, ct);

            _logger.LogInformation(
                "Dispatched held {Stage} mail for application {ApplicationUid}.", mail.ToStage, applicationUid);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogInformation(
                "Held mail for application {ApplicationUid} changed while dispatching; the newer move stands.",
                applicationUid);
        }
        catch (InvalidOperationException ex)
        {
            // No slug or no template on the slot. Not retried: a decision mail that arrives days
            // later, after someone fixes the slot, is worse than none. The row records why.
            _logger.LogCritical(ex,
                "Held {Stage} mail for application {ApplicationUid} could not be sent.", mail.ToStage, applicationUid);

            dbContext.ChangeTracker.Clear();

            PendingStageMail? fresh = await dbContext.PendingStageMails
                .FirstOrDefaultAsync(m => m.ApplicationUid == applicationUid, ct);

            if (fresh is { Status: PendingStageMailStatus.Pending })
            {
                fresh.MarkFailed(ex.Message);
                await dbContext.SaveChangesAsync(ct);
            }
        }
    }
}
