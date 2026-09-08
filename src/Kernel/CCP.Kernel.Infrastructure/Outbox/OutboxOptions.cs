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
}
