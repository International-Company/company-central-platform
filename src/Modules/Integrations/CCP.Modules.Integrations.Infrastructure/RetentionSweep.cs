using CCP.Kernel.Application.Configuration;
using CCP.Kernel.Application.Jobs;
using CCP.Kernel.Primitives;
using CCP.Modules.Integrations.Application;
using CCP.Modules.Integrations.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CCP.Modules.Integrations.Infrastructure;

/// <summary>
/// Removes call log rows and webhook receipts once they are past their use.
/// <para>
/// <b>Written on the first day rather than the day somebody notices.</b> The
/// call log grows with traffic rather than with the company: a single provider
/// called a few times a second produces more rows in a month than the employee
/// table will hold in a decade. A retention policy added later is added after
/// the table is already the largest in the database and the delete is already
/// the most expensive statement anybody has run against it (§19.4).
/// </para>
/// <para>
/// The two are swept on very different clocks. A call log entry is evidence and
/// is kept for months; a webhook receipt exists only to notice a replay, and
/// nothing older than the tolerance window can be replayed — keeping one longer
/// is storing a row to answer a question the clock has already answered.
/// </para>
/// </summary>
public sealed class RetentionSweep(
    IServiceScopeFactory scopeFactory,
    IntegrationOptions options,
    IPlatformSettings settings,
    IClock clock,
    JobRunner jobs) : BackgroundService
{
    /// <summary>
    /// The name this sweep is known by in the job history.
    /// <para>
    /// Written down rather than taken from the type name, so renaming the class
    /// cannot start a fresh history and leave the old one looking as though the
    /// sweep stopped running.
    /// </para>
    /// </summary>
    public const string JobName = "integrations.retention";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nothing on start. The first pass on a long-unpruned table is the
        // expensive one, and running it while the rest of the Platform is still
        // warming up is the worst moment available.
        using var timer = new PeriodicTimer(options.RetentionSweepInterval);

        // The runner times the pass, records its outcome and swallows whatever
        // it throws — a sweep that threw out of here would stop its own timer
        // for the life of the process.
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await jobs.RunAsync(JobName, SweepAsync, stoppingToken);
        }
    }

    private async Task<string?> SweepAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IIntegrationRepository>();

        DateTimeOffset now = clock.UtcNow;

        // Read on every pass, so changing how long the call log is kept takes
        // effect on the next sweep rather than on the next deployment. The
        // configured value is the fallback: a retention of zero would empty the
        // log, so an unreadable setting must never produce one.
        TimeSpan callLogRetention = await settings.GetDurationAsync(
            PlatformSettingKeys.IntegrationCallLogRetention,
            options.CallLogRetention,
            cancellationToken);

        // Twice the tolerance window, so a receipt is never dropped while the
        // request it remembers could still arrive again.
        DateTimeOffset receiptsBefore = now - (options.WebhookTolerance * 2);

        (int calls, int receipts) = await repository.PurgeExpiredAsync(
            now - callLogRetention,
            receiptsBefore,
            options.RetentionBatchSize,
            cancellationToken);

        // Null when there was nothing to do, which is the ordinary case. A
        // history in which every line says "removed 0 rows" is one nobody reads
        // closely enough to notice the line that says something else.
        return calls == 0 && receipts == 0
            ? null
            : FormattableString.Invariant(
                $"Removed {calls} call log entries and {receipts} webhook receipts.");
    }
}
