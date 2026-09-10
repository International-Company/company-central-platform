using CCP.Kernel.Application.Jobs;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Documents.Application;
using CCP.Modules.Documents.Application.Abstractions;
using CCP.Modules.Documents.Domain.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CCP.Modules.Documents.Infrastructure;

/// <summary>
/// Destroys the content of documents whose grace period has run out.
/// <para>
/// The second half of two-stage deletion (§18.5), and the only thing in the
/// Platform that removes data irreversibly. It runs on a timer rather than on a
/// request because "delete this in thirty days" is not something a request can
/// wait around for, and because nobody should have to press a button for a
/// promise the company already made.
/// </para>
/// </summary>
public sealed class PurgeSweep(
    IServiceScopeFactory scopeFactory,
    IDocumentStorageProvider storage,
    DocumentOptions options,
    IClock clock,
    JobRunner jobs,
    ILogger<PurgeSweep> logger) : BackgroundService
{
    /// <summary>The name this sweep is known by in the job history.</summary>
    public const string JobName = "documents.purge";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A first pass immediately on start would run during deployment, when
        // half the platform is still warming up, to do the one job that cannot
        // be undone. It can wait an interval.
        using var timer = new PeriodicTimer(options.PurgeSweepInterval);

        // The runner times the pass, records its outcome and swallows whatever
        // it throws: the next pass finds the same documents and tries again,
        // which is exactly what should happen. A throw out of here would stop
        // the timer for the life of the process, and the one job that would
        // stop is the one that keeps a deletion promise.
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await jobs.RunAsync(JobName, SweepAsync, stoppingToken);
        }
    }

    private async Task<string?> SweepAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();

        IDocumentRepository repository =
            scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        IDocumentUnitOfWork unitOfWork =
            scope.ServiceProvider.GetRequiredService<IDocumentUnitOfWork>();

        DateTimeOffset now = clock.UtcNow;

        IReadOnlyList<Document> due = await repository.GetDuePurgesAsync(
            now, options.PurgeBatchSize, cancellationToken);

        if (due.Count == 0)
        {
            return null;
        }

        int refused = 0;

        foreach (Document document in due)
        {
            // The bytes go first, and the record follows. The other order can
            // leave a document marked as destroyed whose content is still in the
            // bucket — a false statement in the one place a company will be
            // asked to prove something. This order can leave content whose row
            // still says it exists, and the next sweep deletes it again:
            // removal is idempotent precisely so that retrying is safe.
            foreach (DocumentVersion version in document.Versions)
            {
                if (!version.ContentRemoved)
                {
                    await storage.DeleteAsync(version.ObjectKey, cancellationToken);
                }
            }

            Result purged = document.Purge(now);

            if (purged.IsFailure)
            {
                refused++;

                logger.LogWarning(
                    "Document {DocumentId} was due for purge and refused: {Error}",
                    document.Id, purged.Error.Code);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The refusals are named in the summary rather than only in the log.
        // This is the one sweep that destroys data, so "purged 40, refused 3" is
        // the difference between a pass that worked and one that needs looking
        // at — and a count in the history is seen, where a warning in a log is
        // read only by whoever went looking.
        return refused == 0
            ? FormattableString.Invariant($"Purged the content of {due.Count} document(s).")
            : FormattableString.Invariant(
                $"Purged the content of {due.Count - refused} document(s); {refused} refused.");
    }
}
