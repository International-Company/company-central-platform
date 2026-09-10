using CCP.Kernel.Application.Jobs;
using CCP.Kernel.Primitives;
using CCP.Modules.Audit.Application;
using CCP.Modules.Audit.Application.Abstractions;
using CCP.Modules.Audit.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Audit.Infrastructure;

/// <summary>
/// The seam every module writes through.
/// <para>
/// <b>A failure here is logged, never thrown.</b> An audit write must not fail
/// the operation it is recording (ARCHITECTURE.md §15.6): refusing a legitimate
/// role grant because the trail was briefly unwritable trades a real capability
/// for a record nobody asked to prioritise that way.
/// </para>
/// <para>
/// That is a genuine trade, not a free one — a swallowed failure is a lost
/// event. It is logged at error level precisely so that "the trail is behind"
/// is an alert someone sees rather than a silence. The durable answer is the
/// outbox path, where events are committed with the change they describe and
/// dispatched afterwards; this direct path is for the events that have no
/// transaction to ride along with, such as a denial.
/// </para>
/// </summary>
public sealed class AuditRecorder(
    IAuditRepository repository,
    ILogger<AuditRecorder> logger) : IAuditRecorder
{
    public Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        return RecordAsync([auditEvent], cancellationToken);
    }

    public async Task RecordAsync(
        IReadOnlyList<AuditEvent> auditEvents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvents);

        if (auditEvents.Count == 0)
        {
            return;
        }

        try
        {
            await repository.AppendAsync(auditEvents, cancellationToken);
        }
#pragma warning disable CA1031
        // Every exception, deliberately. The contract of this method is that the
        // caller's operation survives whatever happens here, and narrowing the
        // catch would leave some database fault able to fail a role grant.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            logger.LogError(
                exception,
                "Failed to write {Count} audit event(s). The trail is incomplete for {Actions}.",
                auditEvents.Count,
                string.Join(", ", auditEvents.Select(e => e.Action).Distinct()));
        }
    }
}

/// <summary>
/// Keeps monthly partitions ready ahead of time.
/// <para>
/// A partitioned table rejects any row with no partition to hold it. Creating
/// them on demand would mean the first write of a new month fails, and the first
/// minute of a new month is the worst moment to discover that — so they are
/// created ahead, on startup and daily thereafter.
/// </para>
/// <para>
/// Daily rather than monthly because a deployment that runs for months without a
/// restart still needs new partitions, and a job that only fires at a boundary
/// is a job that fires when nobody is watching.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>It resolves a scope per pass rather than taking the repository directly.</b>
/// A hosted service is a singleton and the repository is scoped, so injecting it
/// would be a captive dependency: one instance, created at startup, held for the
/// lifetime of the process. The container refuses to build at all when scope
/// validation is on — which is how this was caught, by the integration suite,
/// before it reached a deployment where validation is off by default and the
/// same mistake would merely have been silent.
/// </para>
/// </remarks>
public sealed class AuditPartitionMaintenance(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IOptions<AuditOptions> options,
    ILogger<AuditPartitionMaintenance> logger) : BackgroundService
{
    private readonly AuditOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                DateTimeOffset now = clock.UtcNow;

                using IServiceScope scope = scopeFactory.CreateScope();

                IAuditRepository repository =
                    scope.ServiceProvider.GetRequiredService<IAuditRepository>();

                // One month behind as well as several ahead: an event may arrive
                // late, and a partition that no longer exists for last month
                // would reject it.
                await repository.EnsurePartitionsAsync(
                    now.AddMonths(-1),
                    now.AddMonths(_options.PartitionsAheadMonths),
                    stoppingToken);
            }
#pragma warning disable CA1031
            // The loop must outlive any single failure. A maintenance job that
            // dies on one transient database error stops creating partitions
            // altogether, and the symptom appears weeks later as rejected
            // inserts.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                logger.LogError(exception, "Audit partition maintenance failed. Retrying tomorrow.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
