using CCP.Kernel.Application.Jobs;
using CCP.Kernel.Infrastructure.Persistence;
using CCP.Kernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CCP.Kernel.Infrastructure.Jobs;

/// <summary>
/// Writes the job history to <c>kernel.job_runs</c>.
/// <para>
/// <b>In its own scope and its own transaction, always.</b> The run being
/// recorded is usually a sweep that just deleted rows in a module's schema, and
/// enrolling the record in that work's transaction would mean the record of a
/// failed pass rolls back along with the pass — losing exactly the evidence
/// somebody will come looking for. The same reasoning that keeps a refused
/// document access committed on its own.
/// </para>
/// </summary>
public sealed class JobJournal(
    IServiceScopeFactory scopeFactory,
    IOptions<JobJournalOptions> options,
    IClock clock) : IJobJournal, IDisposable
{
    /// <summary>
    /// Which process wrote the row.
    /// <para>
    /// The machine name, which on a container is the container id — different
    /// on every instance and every restart, which is what makes it useful.
    /// Truncated because the column is bounded and a host name is not.
    /// </para>
    /// </summary>
    private static readonly string InstanceName =
        Truncate(Environment.MachineName, 100) ?? "unknown";

    private readonly JobJournalOptions _options = options.Value;

    /// <summary>
    /// When the history was last pruned. Compared under a lock so that two
    /// jobs finishing in the same second do not both start a delete.
    /// </summary>
    private DateTimeOffset _lastPruned = DateTimeOffset.MinValue;

    private readonly SemaphoreSlim _pruneGate = new(1, 1);

    public async Task RecordAsync(JobRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        using IServiceScope scope = scopeFactory.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<KernelDbContext>();

        context.JobRuns.Add(new JobRunRecord
        {
            Id = Uuid7.NewGuid(run.StartedAt),
            Job = Truncate(run.Job, 100) ?? run.Job,
            StartedAt = run.StartedAt,
            DurationMs = run.DurationMs,
            Outcome = run.Outcome,
            Summary = Truncate(run.Summary, 1000),
            Error = Truncate(run.Error, 2000),
            Instance = InstanceName,
        });

        await context.SaveChangesAsync(cancellationToken);

        await PruneIfDueAsync(context, cancellationToken);
    }

    /// <summary>
    /// Removes runs past their retention, at most once per prune interval.
    /// <para>
    /// Piggy-backed on a write rather than given its own timer, because a job
    /// history kept bounded by a background job would have one job whose failure
    /// nothing records — and it is the job whose failure fills the disk.
    /// </para>
    /// </summary>
    private async Task PruneIfDueAsync(KernelDbContext context, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;

        if (now - _lastPruned < _options.PruneInterval)
        {
            return;
        }

        if (!await _pruneGate.WaitAsync(0, cancellationToken))
        {
            // Another job is already pruning. One delete is enough.
            return;
        }

        try
        {
            if (now - _lastPruned < _options.PruneInterval)
            {
                return;
            }

            _lastPruned = now;

            DateTimeOffset cutoff = now - _options.Retention;

            await context.JobRuns
                .Where(record => record.StartedAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
        }
        finally
        {
            _pruneGate.Release();
        }
    }

    /// <summary>
    /// Bounds a value to its column.
    /// <para>
    /// There is one of these rather than a non-null and a nullable overload,
    /// because nullability annotations do not distinguish two signatures and
    /// the pair does not compile.
    /// </para>
    /// </summary>
    private static string? Truncate(string? value, int length) =>
        value is null || value.Length <= length ? value : value[..length];

    /// <summary>Releases the gate that keeps two jobs from pruning at once.</summary>
    public void Dispose() => _pruneGate.Dispose();
}
