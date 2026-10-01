namespace Kariyer.Mail.Api.Common.Enums;

public enum PendingStageMailStatus
{
    /// <summary>Waiting out the hold; the dispatch job sends it once <c>DueAt</c> passes.</summary>
    Pending = 1,

    /// <summary>Handed to the dispatcher. Nothing about this row changes afterwards except a newer move.</summary>
    Dispatched = 2,

    /// <summary>A later move made it moot before the hold ran out — the correction case.</summary>
    Cancelled = 3,

    /// <summary>Could not be sent (no template on the slot when it came due). Logged, never retried.</summary>
    Failed = 4
}
