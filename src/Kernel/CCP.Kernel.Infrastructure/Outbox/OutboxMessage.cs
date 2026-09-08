namespace CCP.Kernel.Infrastructure.Outbox;

/// <summary>
/// One integration event awaiting or having completed delivery.
/// <para>
/// Rows are written in the producing transaction and relayed afterwards
/// (ARCHITECTURE.md §8.5). The table lives in the <c>kernel</c> schema and is
/// deliberately plain, so that debugging a delivery problem is a SQL query
/// rather than an exercise in reading framework internals.
/// </para>
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>The event's own id. Also the idempotency key for consumers.</summary>
    public Guid Id { get; set; }

    /// <summary>Stable event type, e.g. <c>identity.user.created</c>.</summary>
    public required string EventType { get; set; }

    /// <summary>The .NET type used to deserialize <see cref="Payload"/>.</summary>
    public required string PayloadType { get; set; }

    /// <summary>The serialized event.</summary>
    public required string Payload { get; set; }

    /// <summary>When the event occurred, in UTC.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Correlation id of the request that produced the event.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Null until delivered. Non-null means every handler succeeded.</summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Number of delivery attempts made so far.</summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Earliest time the relay may attempt delivery. Used for exponential
    /// backoff; a newly written row is due immediately.
    /// </summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    /// <summary>The most recent failure, for diagnosis. Never shown to a caller.</summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Set when the message has failed too many times. A dead-lettered message
    /// is no longer retried and must be inspected by an operator — it is
    /// visible rather than silently discarded.
    /// </summary>
    public DateTimeOffset? DeadLetteredAt { get; set; }
}
