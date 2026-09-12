using System.Globalization;
using CCP.Kernel.Application.Jobs;
using CCP.Kernel.Primitives;
using CCP.Modules.Documents.Application;
using CCP.Modules.Documents.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CCP.Modules.Documents.Infrastructure;

/// <summary>
/// Finds content in the store that no version row refers to.
/// <para>
/// <b>ADR-014 named this on the day it chose two stores, and it is the price of
/// that choice.</b> There is no transaction spanning a bucket and a database, so
/// an upload writes the object and then the row, and a failure between the two
/// leaves content nothing references. That order is deliberate — the reverse
/// leaves a row pointing at nothing, which costs somebody their file, where this
/// costs storage. But "costs storage" is only true while somebody is counting,
/// and until now nobody was.
/// </para>
/// <para>
/// <b>It reports and does not delete, and that is not timidity.</b> An orphan is
/// defined by the database never having heard of it — which is also what every
/// object looks like when the database is not the one that wrote the bucket. A
/// restored backup, a connection string pointed at the wrong environment, a
/// staging deployment sharing production storage: in each case a sweep with
/// delete rights removes every document uploaded since, permanently, and the
/// first anybody hears of it is a person who cannot open their file. The purge
/// sweep destroys data because it is acting on a record that says to. This has
/// no record; it has an absence, and an absence is not an instruction.
/// </para>
/// <para>
/// So the answer it produces is a number in the job history, which is where
/// somebody looks when they want to know what the Platform is doing with its
/// storage bill. Removing the objects is a decision with a name on it.
/// </para>
/// </summary>
public sealed class ReconciliationSweep(
    IServiceScopeFactory scopeFactory,
    IDocumentStorageProvider storage,
    DocumentOptions options,
    IClock clock,
    JobRunner jobs,
    ILogger<ReconciliationSweep> logger) : BackgroundService
{
    /// <summary>The name this sweep is known by in the job history.</summary>
    public const string JobName = "documents.reconcile";

    /// <summary>
    /// How many keys are checked against the database at once.
    /// <para>
    /// Large enough that a bucket of a hundred thousand objects is a few hundred
    /// queries rather than a hundred thousand, and small enough that the
    /// <c>IN</c> list stays something PostgreSQL plans rather than something it
    /// endures.
    /// </para>
    /// </summary>
    private const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.ReconciliationSweepInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await jobs.RunAsync(JobName, SweepAsync, stoppingToken);
        }
    }

    internal async Task<string?> SweepAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();

        IDocumentRepository repository =
            scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        // Anything written more recently than this is left alone, whatever the
        // database says about it.
        //
        // An upload that has stored its object and not yet committed its row is
        // indistinguishable from an orphan by every other measure. Without this
        // the sweep would report — and a deleting version of it would destroy —
        // documents out from under the people uploading them, and it would do so
        // most often on the busiest day.
        DateTimeOffset cutoff = clock.UtcNow - options.OrphanGracePeriod;

        var batch = new List<StoredObject>(BatchSize);

        long scanned = 0;
        long orphans = 0;
        long orphanBytes = 0;
        long tooNew = 0;

        await foreach (StoredObject item in storage.ListAsync(cancellationToken))
        {
            scanned++;

            if (item.LastModifiedAt > cutoff)
            {
                tooNew++;

                continue;
            }

            batch.Add(item);

            if (batch.Count < BatchSize)
            {
                continue;
            }

            (long count, long bytes) = await CheckAsync(repository, batch, cancellationToken);

            orphans += count;
            orphanBytes += bytes;

            batch.Clear();
        }

        if (batch.Count > 0)
        {
            (long count, long bytes) = await CheckAsync(repository, batch, cancellationToken);

            orphans += count;
            orphanBytes += bytes;
        }

        if (orphans == 0)
        {
            // Said rather than left blank. A pass that found nothing and a pass
            // that did not run look identical in a history that only records
            // problems, and telling them apart is most of what this page is for.
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Checked {scanned} object(s); none orphaned. {tooNew} too recent to judge.");
        }

        // A warning as well as a summary. The history answers "is this getting
        // worse"; the log is what an alert can be built on before anybody thinks
        // to open the page.
        logger.LogWarning(
            "{Orphans} stored object(s) totalling {Bytes} bytes are referenced by no document "
            + "version. Nothing has been deleted. See docs/documents/README.md.",
            orphans, orphanBytes);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Checked {scanned} object(s); {orphans} orphaned, {orphanBytes} byte(s). "
            + $"Nothing deleted.");
    }

    private static async Task<(long Count, long Bytes)> CheckAsync(
        IDocumentRepository repository,
        List<StoredObject> batch,
        CancellationToken cancellationToken)
    {
        IReadOnlySet<string> known = await repository.GetKnownObjectKeysAsync(
            [.. batch.Select(item => item.ObjectKey)], cancellationToken);

        long count = 0;
        long bytes = 0;

        foreach (StoredObject item in batch)
        {
            if (known.Contains(item.ObjectKey))
            {
                continue;
            }

            count++;
            bytes += item.Length;
        }

        return (count, bytes);
    }
}
