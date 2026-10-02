namespace Kariyer.Mail.Api.Common.Configuration;

public sealed class RecruitingMailSettings
{
    public const string SectionName = "RecruitingMail";

    /// <summary>
    /// Whether the candidate is mailed about decisions made in the company panel — offer, hire,
    /// rejection. OFF by default: the product does not tell candidates about panel decisions yet.
    ///
    /// Off means nothing is held and nothing is sent; the events are still consumed so the queue
    /// does not grow, and the slots stay in the admin panel so templates can be prepared before
    /// this is switched on. Interview mail and the submitted / withdrawn mails are not affected.
    /// </summary>
    public bool StageMailsEnabled { get; init; }

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
