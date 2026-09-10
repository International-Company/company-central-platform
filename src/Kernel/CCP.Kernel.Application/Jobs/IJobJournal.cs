namespace CCP.Kernel.Application.Jobs;

/// <summary>How a background run ended.</summary>
public enum JobOutcome
{
    Succeeded = 1,

    Failed = 2,

    /// <summary>
    /// Stopped because the host is shutting down.
    /// <para>
    /// Distinct from a failure on purpose. A sweep cancelled by a deployment did
    /// not go wrong, and counting it as a failure would make every release look
    /// like an incident — which trains people to ignore the one that is.
    /// </para>
    /// </summary>
    Cancelled = 3
}

/// <summary>
/// One execution of a background job, as it is recorded.
/// </summary>
/// <param name="Job">
/// A stable name, e.g. <c>integrations.retention</c>. It is the identity of the
/// job across restarts and deployments, so it is written down rather than
/// derived from a type name that a rename would silently change.
/// </param>
/// <param name="StartedAt">When the run began.</param>
/// <param name="DurationMs">How long it took, successful or not.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="Summary">
/// What it did, in one line, from the job itself — "removed 412 call log
/// entries". The count is the difference between knowing a sweep ran and
/// knowing it worked.
/// </param>
/// <param name="Error">
/// The failure, if there was one. The message and type only; a stack trace
/// belongs in the log, which has the correlation id to find it by.
/// </param>
public sealed record JobRun(
    string Job,
    DateTimeOffset StartedAt,
    double DurationMs,
    JobOutcome Outcome,
    string? Summary = null,
    string? Error = null);

/// <summary>
/// Where a background job says what it did.
/// <para>
/// <b>In the kernel because jobs are not in one module.</b> Five sweeps live in
/// five module Infrastructure projects, and no module may reference another
/// (ARCHITECTURE.md §6.2). The same shape as <c>IAuditTrail</c>: the kernel owns
/// the contract, one implementation owns the behaviour.
/// </para>
/// <para>
/// <b>Why a table and not only a metric.</b> Phase 14 emits <c>ccp.jobs.runs</c>,
/// which answers "how often" and "how long" in aggregate. It cannot answer the
/// question actually asked at nine in the morning — <i>did last night's purge
/// run, and what did it do?</i> — because a metrics backend keeps a rate, not a
/// row, and the Platform must be able to answer that with no external system
/// attached to it at all.
/// </para>
/// <para>
/// <b>Implementations must not throw.</b> A journal write must never fail the
/// job it is recording. Refusing to sweep expired rows because the record of the
/// sweep was unwritable trades the work for the paperwork.
/// </para>
/// </summary>
public interface IJobJournal
{
    Task RecordAsync(JobRun run, CancellationToken cancellationToken = default);
}

/// <summary>
/// The journal when none is registered.
/// <para>
/// Records nothing, which is honest. The alternative is every job checking
/// whether journalling exists before it starts, and that check being wrong
/// somewhere.
/// </para>
/// </summary>
public sealed class NullJobJournal : IJobJournal
{
    public Task RecordAsync(JobRun run, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
