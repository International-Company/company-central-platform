using CCP.Kernel.Primitives;
using CCP.Modules.Integrations.Application;
using CCP.Modules.Integrations.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
    IClock clock,
    ILogger<RetentionSweep> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nothing on start. The first pass on a long-unpruned table is the
        // expensive one, and running it while the rest of the Platform is still
        // warming up is the worst moment available.
        using var timer = new PeriodicTimer(options.RetentionSweepInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // A failed sweep must not take the host down or stop the timer.
                // The next pass finds the same rows and tries again, which is
                // exactly what should happen.
                logger.LogError(exception, "The integration retention sweep failed.");
            }
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IIntegrationRepository>();

        DateTimeOffset now = clock.UtcNow;

        // Twice the tolerance window, so a receipt is never dropped while the
        // request it remembers could still arrive again.
        DateTimeOffset receiptsBefore = now - (options.WebhookTolerance * 2);

        (int calls, int receipts) = await repository.PurgeExpiredAsync(
            now - options.CallLogRetention,
            receiptsBefore,
            options.RetentionBatchSize,
            cancellationToken);

        if ((calls > 0 || receipts > 0) && logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Integration retention removed {Calls} call log entries and {Receipts} webhook receipts.",
                calls, receipts);
        }
    }
}
