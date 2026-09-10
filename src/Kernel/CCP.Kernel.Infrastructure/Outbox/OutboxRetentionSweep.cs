using CCP.Kernel.Application.Jobs;
using CCP.Kernel.Infrastructure.Persistence;
using CCP.Kernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CCP.Kernel.Infrastructure.Outbox;

/// <summary>
/// Removes outbox rows that have done their job.
/// <para>
/// <b>The oldest open debt in the project, and it was open because the table
/// looks harmless.</b> Every state change in every module writes a row here, so
/// the outbox grows with the company's activity and never with its size — and a
/// processed row is kept only so that a crash mid-pass cannot lose an event. Once
/// it is delivered and the transaction is committed, it is a receipt for
/// something nobody will ask about.
/// </para>
/// <para>
/// <b>Dead-lettered rows are never removed here.</b> A dead letter is an event
/// that will never be delivered, which means an audit entry or a notification is
/// permanently missing and somebody has to decide what to do about it. Sweeping
/// those away on a timer would quietly erase the evidence of the one failure
/// mode this whole mechanism exists to make visible — so they stay until a
/// person deals with them, and the Operations screen counts them.
/// </para>
/// <para>
/// It deletes in bounded batches. The first pass on a table nobody has pruned is
/// the expensive one, and a single unbounded <c>DELETE</c> across millions of
/// rows holds locks and bloats the write-ahead log for as long as it runs.
/// </para>
/// </summary>
public sealed class OutboxRetentionSweep(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    IClock clock,
    JobRunner jobs) : BackgroundService
{
    /// <summary>The name this sweep is known by in the job history.</summary>
    public const string JobName = "kernel.outbox-retention";

    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nothing on start. The first pass on a long-unpruned table is the
        // expensive one, and running it while the rest of the Platform is still
        // warming up is the worst moment available.
        using var timer = new PeriodicTimer(_options.RetentionSweepInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await jobs.RunAsync(JobName, SweepAsync, stoppingToken);
        }
    }

    private async Task<string?> SweepAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<KernelDbContext>();

        DateTimeOffset cutoff = clock.UtcNow - _options.ProcessedRetention;

        int removed = 0;

        // Batched, and it stops when a pass comes back short. Deleting until the
        // table is clean in one go would be one long transaction on the busiest
        // table in the database; deleting a bounded slice each tick lets a large
        // backlog drain over hours without anybody noticing.
        while (removed < _options.RetentionMaxRowsPerPass)
        {
            int batch = await context.OutboxMessages
                .Where(message =>
                    message.ProcessedAt != null
                    && message.ProcessedAt < cutoff
                    && message.DeadLetteredAt == null)
                .OrderBy(message => message.ProcessedAt)
                .Take(_options.RetentionBatchSize)
                .ExecuteDeleteAsync(cancellationToken);

            removed += batch;

            if (batch < _options.RetentionBatchSize)
            {
                break;
            }
        }

        // Null when there was nothing to do, which is the ordinary case. A
        // history in which every line says "removed 0 rows" is one nobody reads
        // closely enough to notice the line that says something else.
        return removed == 0
            ? null
            : FormattableString.Invariant($"Removed {removed} delivered outbox message(s).");
    }
}
