using CCP.Kernel.Primitives;
using CCP.Modules.Notifications.Application.Abstractions;
using CCP.Modules.Notifications.Domain.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Notifications.Infrastructure.Dispatch;

/// <summary>How hard and how often to try.</summary>
public sealed class DispatchOptions
{
    public const string SectionName = "Notifications:Dispatch";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// How many times to try before giving up.
    /// <para>
    /// Five, over roughly half an hour with the backoff below. Long enough to
    /// ride out a mail server restart, short enough that a genuinely dead
    /// address surfaces in administration the same morning rather than next
    /// week.
    /// </para>
    /// </summary>
    public int MaximumAttempts { get; set; } = 5;

    /// <summary>The first wait. Each retry roughly doubles it.</summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(30);

    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Delivers what has been queued.
/// <para>
/// <b>Sending enqueues; this does the slow part</b> (§17.4). Talking to a mail
/// server is slow and fails in ways that have nothing to do with the action that
/// caused the message, so it happens here — where a retry costs nobody's request
/// and a dead provider does not make approving a purchase order time out.
/// </para>
/// <para>
/// <b>Retry, backoff and giving up live here rather than in the providers.</b>
/// One policy, so every channel behaves the same when a vendor is down, and a
/// provider written next year cannot invent its own by accident.
/// </para>
/// <para>
/// Backoff is exponential with jitter. Without the jitter, a mail server coming
/// back after an outage is met by every queued message at the same instant —
/// which is how a recovery becomes a second outage.
/// </para>
/// </summary>
public sealed class NotificationDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<DispatchOptions> options,
    IClock clock,
    ILogger<NotificationDispatcher> logger) : BackgroundService
{
    private readonly DispatchOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Notification dispatch is disabled by configuration.");

            return;
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Notification dispatcher started. PollInterval={PollInterval} BatchSize={BatchSize}",
                _options.PollInterval.ToString(),
                _options.BatchSize);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            int handled = 0;

            try
            {
                handled = await DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // A dispatcher must not die of one bad pass.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                // Logged and swallowed. A background worker that stops on one
                // transient database error silently ends notification for the
                // life of the process, and nobody notices until somebody asks
                // why they never heard about an approval.
                logger.LogError(exception, "The notification dispatcher failed a pass. Retrying.");
            }

            // A full batch suggests a backlog, so continue rather than sleeping
            // through it.
            if (handled < _options.BatchSize)
            {
                await Task.Delay(_options.PollInterval, stoppingToken);
            }
        }
    }

    private async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<INotificationUnitOfWork>();

        IReadOnlyList<INotificationChannelProvider> providers =
            [.. scope.ServiceProvider.GetServices<INotificationChannelProvider>()];

        IReadOnlyList<Notification> pending =
            await repository.GetPendingAsync(_options.BatchSize, cancellationToken);

        if (pending.Count == 0)
        {
            return 0;
        }

        DateTimeOffset now = clock.UtcNow;
        int handled = 0;

        foreach (Notification notification in pending)
        {
            if (!IsDue(notification, now))
            {
                continue;
            }

            INotificationChannelProvider? provider = providers.FirstOrDefault(
                p => p.Channel == notification.Channel);

            if (provider is null)
            {
                // Nothing can deliver this. Abandoned rather than retried
                // forever: a channel with no provider is a configuration
                // problem, and burning five attempts on it hides that behind a
                // delay instead of surfacing it.
                notification.Abandon(now);

                logger.LogWarning(
                    "No provider is registered for the {Channel} channel. "
                    + "Notification {Id} was abandoned.",
                    notification.Channel, notification.Id);

                handled++;

                continue;
            }

            long started = TimeProvider.System.GetTimestamp();

            DeliveryOutcome outcome = await provider.SendAsync(notification, cancellationToken);

            TimeSpan duration = TimeProvider.System.GetElapsedTime(started);

            notification.RecordAttempt(
                outcome.Succeeded, provider.Name, outcome.Response, duration, clock.UtcNow);

            if (!outcome.Succeeded
                && (outcome.IsPermanent || notification.Deliveries.Count >= _options.MaximumAttempts))
            {
                // Permanent failures are not retried at all: a malformed address
                // stays malformed however many times it is tried, and retrying
                // it for half an hour delays every message behind it.
                notification.Abandon(clock.UtcNow);

                logger.LogWarning(
                    "Notification {Id} to {Recipient} on {Channel} failed permanently "
                    + "after {Attempts} attempt(s): {Response}",
                    notification.Id, notification.RecipientUserId, notification.Channel,
                    notification.Deliveries.Count, outcome.Response);
            }

            handled++;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return handled;
    }

    /// <summary>
    /// Whether enough time has passed since the last attempt.
    /// <para>
    /// Exponential with jitter, computed from the attempt count rather than
    /// stored: a `NextAttemptAt` column would be a second place for the schedule
    /// to live and a second place for it to be wrong.
    /// </para>
    /// </summary>
    private bool IsDue(Notification notification, DateTimeOffset now)
    {
        NotificationDelivery? last = notification.Deliveries
            .OrderByDescending(d => d.Attempt)
            .FirstOrDefault();

        if (last is null)
        {
            return true;
        }

        double seconds = _options.InitialBackoff.TotalSeconds * Math.Pow(2, last.Attempt - 1);

        // Up to a quarter more, so a provider coming back after an outage is not
        // met by every queued message at the same instant.
        double jittered = seconds * (1 + (Random.Shared.NextDouble() * 0.25));

        return now >= last.OccurredAt.AddSeconds(jittered);
    }
}
