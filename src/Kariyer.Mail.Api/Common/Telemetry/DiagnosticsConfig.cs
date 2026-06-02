using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Kariyer.Mail.Api.Common.Telemetry;

public static class DiagnosticsConfig
{
    public const string ServiceName = "mail-service";

    public static readonly ActivitySource MailActivitySource = new(ServiceName);
    public static readonly Meter MailMeter = new(ServiceName);

    // ── Dispatch metrics ────────────────────────────────────────────────────────
    public static readonly Counter<int> EmailsSentCounter = MailMeter.CreateCounter<int>(
        "kariyer.mail.emails_sent",
        description: "Number of emails successfully dispatched");

    public static readonly Counter<int> EmailsFailedCounter = MailMeter.CreateCounter<int>(
        "kariyer.mail.emails_failed",
        description: "Number of email dispatches that failed");

    public static readonly Histogram<double> EmailSendDuration = MailMeter.CreateHistogram<double>(
        "kariyer.mail.email_send_duration_ms",
        unit: "ms",
        description: "Time taken by the active email provider to accept the message");

    // ── Bulk job metrics ─────────────────────────────────────────────────────────
    public static readonly Counter<int> BulkJobsStartedCounter = MailMeter.CreateCounter<int>(
        "kariyer.mail.bulk_jobs_started",
        description: "Number of bulk email jobs created");

    public static readonly Counter<int> BulkJobsCompletedCounter = MailMeter.CreateCounter<int>(
        "kariyer.mail.bulk_jobs_completed",
        description: "Number of bulk email jobs that completed successfully");

    public static readonly Counter<int> BulkJobsFailedCounter = MailMeter.CreateCounter<int>(
        "kariyer.mail.bulk_jobs_failed",
        description: "Number of bulk email jobs that failed");

    public static readonly Counter<int> BulkJobsCancelledCounter = MailMeter.CreateCounter<int>(
        "kariyer.mail.bulk_jobs_cancelled",
        description: "Number of bulk email jobs that were cancelled");

    // ── Resolution metrics ───────────────────────────────────────────────────────
    public static readonly Counter<int> TargetsResolvedCounter = MailMeter.CreateCounter<int>(
        "kariyer.mail.targets_resolved",
        description: "Number of email targets resolved from the legacy system");

    public static readonly Histogram<double> ResolutionBatchDuration = MailMeter.CreateHistogram<double>(
        "kariyer.mail.resolution_batch_duration_ms",
        unit: "ms",
        description: "Time taken to fetch, insert, and queue a single batch of targets");

    // ── Template cache metrics ───────────────────────────────────────────────────
    public static readonly Counter<int> TemplateCacheHitsCounter = MailMeter.CreateCounter<int>(
        "kariyer.mail.template_cache_hits",
        description: "Number of template cache hits from Garnet");

    public static readonly Counter<int> TemplateCacheMissesCounter = MailMeter.CreateCounter<int>(
        "kariyer.mail.template_cache_misses",
        description: "Number of template cache misses (fell through to PostgreSQL)");

    public static readonly Counter<int> TemplateNotFoundCounter = MailMeter.CreateCounter<int>(
        "kariyer.mail.template_not_found",
        description: "Number of times a template slug resolved to null (missing template)");

    // ── Idempotency metrics ──────────────────────────────────────────────────────
    public static readonly Counter<int> IdempotencyBlockedCounter = MailMeter.CreateCounter<int>(
        "kariyer.mail.idempotency_blocked",
        description: "Number of requests blocked by the idempotency layer");

    // ── Transactional email metrics ──────────────────────────────────────────────
    public static readonly Counter<int> TransactionalEmailsSentCounter = MailMeter.CreateCounter<int>(
        "kariyer.mail.transactional_emails_sent",
        description: "Number of single transactional emails accepted");

    // ── Schedule metrics ─────────────────────────────────────────────────────────
    public static readonly Counter<long> ScheduledJobsTriggeredCounter = MailMeter.CreateCounter<long>(
        "mail.scheduled_jobs_triggered",
        description: "Number of jobs triggered from schedules");

    // ── Messaging metrics ────────────────────────────────────────────────────────
    public static readonly Counter<int> MessagesConsumedCounter = MailMeter.CreateCounter<int>(
        "kariyer.mail.messages_consumed",
        description: "Number of MassTransit messages consumed (by type and status)");

    public static readonly Counter<int> MessageConsumeFaultsCounter = MailMeter.CreateCounter<int>(
        "kariyer.mail.message_consume_faults",
        description: "Number of MassTransit message consume faults (by type and exception)");

    // ── Retention metrics ────────────────────────────────────────────────────────
    public static readonly Counter<long> TargetsDeletedCounter = MailMeter.CreateCounter<long>(
        "kariyer.mail.targets_deleted",
        description: "Number of EmailTarget rows removed by the retention job");
}
