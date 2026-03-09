using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Kariyer.Mail.Api.Common.Telemetry;

public static class DiagnosticsConfig
{
    public const string ServiceName = "Kariyer.Mail";
    
    public static readonly ActivitySource MailActivitySource = new (ServiceName);

    public static readonly Meter MailMeter = new (ServiceName);
    
    public static readonly Counter<int> EmailsSentCounter = MailMeter.CreateCounter<int>(
        "kariyer.mail.emails_sent", 
        description: "Counts the number of successfully dispatched emails");

    public static readonly Counter<int> EmailsFailedCounter = MailMeter.CreateCounter<int>(
        "kariyer.mail.emails_failed", 
        description: "Counts the number of failed email dispatches");

    public static readonly Counter<int> TargetsResolvedCounter = MailMeter.CreateCounter<int>(
        "kariyer.mail.targets_resolved",
        description: "Counts the number of user targets resolved from the legacy system");

    public static readonly Counter<long> ScheduledJobsTriggeredCounter = MailMeter.CreateCounter<long>(
        "mail.scheduled_jobs_triggered",
        description: "Counts the number of jobs triggered from scheduling");
    
    public static readonly Histogram<double> ResolutionBatchDuration = MailMeter.CreateHistogram<double>(
        "kariyer.mail.resolution_batch_duration_ms",
        unit: "ms",
        description: "Measures the time taken to fetch, insert, and queue a single batch of targets");
}