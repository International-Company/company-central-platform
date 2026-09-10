namespace CCP.Kernel.Infrastructure.Outbox;

/// <summary>Tuning for the outbox relay. Bound from configuration.</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>How often the relay looks for due messages.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How many messages one relay pass claims. Bounded so a backlog is worked
    /// through steadily rather than in one long transaction that blocks.
    /// </summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Attempts before a message is dead-lettered. After this it stops being
    /// retried and becomes an operator's problem, deliberately visibly.
    /// </summary>
    public int MaxAttempts { get; set; } = 8;

    /// <summary>Base delay for exponential backoff between attempts.</summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Upper bound on the backoff delay.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long a delivered message is kept before it is removed.
    /// <para>
    /// Seven days. A processed row exists so that a crash between dispatch and
    /// the row update causes a redelivery rather than a lost event; once it is
    /// committed and delivered, it is a receipt. A week is long enough to
    /// investigate "did that event actually go out on Tuesday" and short enough
    /// that the busiest table in the database does not become the largest one.
    /// </para>
    /// <para>
    /// This never applies to dead-lettered rows. Those are the failures somebody
    /// has to act on, and a timer must not erase them.
    /// </para>
    /// </summary>
    public TimeSpan ProcessedRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>How often the retention sweep runs.</summary>
    public TimeSpan RetentionSweepInterval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// How many rows one <c>DELETE</c> removes.
    /// <para>
    /// Bounded so the statement holds its locks briefly. An unbounded delete
    /// across a table nobody has ever pruned is one long transaction on the
    /// busiest table in the database.
    /// </para>
    /// </summary>
    public int RetentionBatchSize { get; set; } = 1000;

    /// <summary>
    /// The most a single pass will remove before giving the database a rest.
    /// <para>
    /// A backlog drains over several passes rather than in one. Nobody is
    /// waiting for it, and the alternative is a sweep that runs for an hour the
    /// first time it is ever enabled.
    /// </para>
    /// </summary>
    public int RetentionMaxRowsPerPass { get; set; } = 50_000;
}
