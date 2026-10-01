namespace Kariyer.Mail.Api.Common.Configuration;

public sealed class RecruitingMailSettings
{
    public const string SectionName = "RecruitingMail";

    /// <summary>
    /// How long an offer / hire / rejection mail waits before it is sent. A recruiter who
    /// corrects a move within this window never reaches the candidate; after it, the correction
    /// is silent and the first mail stands.
    /// </summary>
    public int StageMailHoldMinutes { get; init; } = 10;

    /// <summary>Hangfire cron for the job that sends held mails once due. Every minute by default.</summary>
    public string DispatchCronExpression { get; init; } = "* * * * *";

    /// <summary>Held mails sent per database round-trip.</summary>
    public int DispatchBatchSize { get; init; } = 100;
}
