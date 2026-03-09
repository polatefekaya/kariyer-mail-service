namespace Kariyer.Mail.Api.Common.Configuration;

/// <summary>
/// Controls the exact physics of the RabbitMQ consumer and the MassTransit batching engine.
/// Do not guess these numbers. They mathematically dictate your RAM usage, database lock times, 
/// and third-party API rate limits.
/// </summary>
public sealed class DispatcherSettings
{
    /// <summary>
    /// The number of messages MassTransit pulls from RabbitMQ into RAM at once across the TCP channel.
    /// 
    /// WHEN TO CHANGE:
    /// - Increase this if your BatchSize is high and you notice the consumer is "starving" (waiting for network I/O from RabbitMQ).
    /// - Decrease this if you have 10 Kubernetes pods running and one pod is hoarding all the messages in memory while the others sit idle.
    /// 
    /// CRITICAL RULE: PrefetchCount MUST be strictly greater than your BatchSize. 
    /// If Prefetch is 50 and BatchSize is 100, your batch will literally never execute.
    /// </summary>
    public int PrefetchCount { get; init; } = 200;

    /// <summary>
    /// The physical array size passed into the DispatchEmailBatchConsumer.
    /// 
    /// WHEN TO CHANGE:
    /// - Increase this to reduce PostgreSQL load. A batch size of 100 means 100 emails are sent, and then updated in Postgres via ONE atomic UNNEST query.
    /// - Decrease this if AWS SES or Mailgun starts throwing 429 Too Many Requests, or if your HTTP provider is slow and the batch is taking longer than the RabbitMQ acknowledge timeout.
    /// </summary>
    public int BatchSize { get; init; } = 100;

    /// <summary>
    /// How many batches MassTransit is allowed to process simultaneously on a single worker node.
    /// 
    /// WHEN TO CHANGE:
    /// - Increase this ONLY if you have the CPU threads and unmanaged HTTP sockets to spare. 
    /// - Decrease this if you are getting "Socket Exhaustion" errors.
    /// 
    /// THE MATH: If BatchSize is 100 and ConcurrencyLimit is 5, this single C# process 
    /// is firing 500 HTTP requests to AWS SES at the exact same time. Multiply that by your Kubernetes pod count.
    /// </summary>
    public int ConcurrencyLimit { get; init; } = 5;

    /// <summary>
    /// The maximum time MassTransit will wait to fill the BatchSize before forcing an execution.
    /// 
    /// WHEN TO CHANGE:
    /// - Increase to 5-10 seconds if you want to aggressively group sporadic transactional emails (like password resets) into larger database commits.
    /// - Keep at 1-2 seconds (or lower) so users don't wait 10 seconds to receive their OTP codes during low-traffic periods.
    /// </summary>
    public int TimeLimitSeconds { get; init; } = 2;
}