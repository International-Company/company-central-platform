using CCP.Kernel.Application.Jobs;

namespace CCP.Kernel.Infrastructure.Jobs;

/// <summary>
/// One row of the job history, in the <c>kernel</c> schema.
/// <para>
/// <b>Not an audit record, and kept apart from one deliberately.</b> The audit
/// trail answers <i>who did what to whom</i> and is append-only, permanently
/// retained and legally interesting. This answers <i>did the machine do its
/// chores</i>, has no actor, and is pruned. Putting operational noise in the
/// audit trail would bury the entries that matter under a hundred rows a day
/// that nobody did.
/// </para>
/// </summary>
public sealed class JobRunRecord
{
    public Guid Id { get; init; }

    /// <summary>The job's stable name, e.g. <c>integrations.retention</c>.</summary>
    public required string Job { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public double DurationMs { get; init; }

    public JobOutcome Outcome { get; init; }

    /// <summary>What the pass did, in the job's own words.</summary>
    public string? Summary { get; init; }

    /// <summary>The failure type and message. The stack trace is in the log.</summary>
    public string? Error { get; init; }

    /// <summary>
    /// Which process ran it.
    /// <para>
    /// Every instance runs every sweep, so on a two-instance deployment the same
    /// job appears twice an hour and the history is unreadable without knowing
    /// which machine each row came from. It also makes "one instance has been
    /// failing since Tuesday" visible, which is otherwise a fifty-per-cent
    /// failure rate that averages away to nothing on a dashboard.
    /// </para>
    /// </summary>
    public required string Instance { get; init; }
}
