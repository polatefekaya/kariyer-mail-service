namespace Kariyer.Mail.Api.Common.Configuration;

public sealed class RetentionSettings
{
    public const string SectionName = "Retention";

    /// <summary>
    /// How many days to keep processed (Sent, Failed, Bounced, Cancelled) targets.
    /// Pending and Queued targets are never touched.
    /// </summary>
    public int TargetRetentionDays { get; init; } = 30;

    /// <summary>
    /// Maximum rows deleted per database round-trip to avoid long-running locks.
    /// </summary>
    public int DeletionBatchSize { get; init; } = 5000;

    /// <summary>
    /// Hangfire cron expression for the cleanup schedule. Defaults to 03:00 daily.
    /// </summary>
    public string CronExpression { get; init; } = "0 3 * * *";
}
