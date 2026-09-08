using System.Text.Json;
using CCP.Kernel.Application.Events;
using CCP.Kernel.Domain;
using CCP.Kernel.Infrastructure.Persistence;
using CCP.Kernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CCP.Kernel.Infrastructure.Outbox;

/// <summary>
/// Delivers outbox messages to their handlers.
/// <para>
/// Claims a batch with <c>FOR UPDATE SKIP LOCKED</c>, which lets several
/// application instances run this relay concurrently without any of them
/// processing the same message and without any of them blocking on a row
/// another instance already holds (ADR-013). No broker, no scheduler, no
/// leader election.
/// </para>
/// <para>
/// Delivery is at-least-once. Handlers must be idempotent, keyed on the event
/// id: a crash between dispatch and the row update causes a redelivery.
/// </para>
/// </summary>
public sealed class OutboxRelay(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    IClock clock,
    ILogger<OutboxRelay> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Outbox relay started. PollInterval={PollInterval} BatchSize={BatchSize}",
                _options.PollInterval.ToString(),
                _options.BatchSize);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int processed = await ProcessBatchAsync(stoppingToken);

                // A full batch suggests a backlog, so continue immediately
                // rather than sleeping through it.
                if (processed < _options.BatchSize)
                {
                    await Task.Delay(_options.PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // The relay must never die. A failure here means messages stop
                // flowing, so it is logged loudly and the loop continues.
                logger.LogError(exception, "Outbox relay pass failed. Retrying after the poll interval.");
                await Task.Delay(_options.PollInterval, stoppingToken);
            }
        }

        logger.LogInformation("Outbox relay stopped.");
    }

    /// <summary>
    /// Processes one batch. Returns the number of messages claimed, which the
    /// loop uses to decide whether to poll again immediately.
    /// </summary>
    internal async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<KernelDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        DateTimeOffset now = clock.UtcNow;

        // SKIP LOCKED is what makes this safe with several instances running:
        // each claims a disjoint set and none waits on rows another holds.
        List<OutboxMessage> batch = await dbContext.OutboxMessages
            .FromSqlRaw(
                "SELECT * FROM kernel.outbox_messages "
                + "WHERE processed_at IS NULL "
                + "  AND dead_lettered_at IS NULL "
                + "  AND next_attempt_at <= {0} "
                + "ORDER BY occurred_at "
                + "LIMIT {1} "
                + "FOR UPDATE SKIP LOCKED",
                now,
                _options.BatchSize)
            .ToListAsync(cancellationToken);

        if (batch.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return 0;
        }

        foreach (OutboxMessage message in batch)
        {
            await DeliverAsync(message, dispatcher, now, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return batch.Count;
    }

    private async Task DeliverAsync(
        OutboxMessage message,
        IIntegrationEventDispatcher dispatcher,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        message.AttemptCount++;

        try
        {
            IIntegrationEvent integrationEvent = Deserialize(message);

            await dispatcher.DispatchAsync(integrationEvent, cancellationToken);

            message.ProcessedAt = now;
            message.LastError = null;

            // Guarded: this runs once per delivered message, and Debug is off
            // in production, so the argument evaluation should not be paid for.
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "Outbox message delivered. Id={MessageId} EventType={EventType} Attempt={Attempt}",
                    message.Id,
                    message.EventType,
                    message.AttemptCount);
            }
        }
        catch (Exception exception)
        {
            message.LastError = Truncate(exception.ToString(), 4000);

            if (message.AttemptCount >= _options.MaxAttempts)
            {
                message.DeadLetteredAt = now;

                // Dead-lettering is an operational event, not a routine one: an
                // event that will never be delivered means an audit or
                // notification record is permanently missing.
                logger.LogError(
                    exception,
                    "Outbox message dead-lettered after {Attempts} attempts. "
                    + "Id={MessageId} EventType={EventType} CorrelationId={CorrelationId}",
                    message.AttemptCount,
                    message.Id,
                    message.EventType,
                    message.CorrelationId);
            }
            else
            {
                message.NextAttemptAt = now.Add(BackoffFor(message.AttemptCount));

                logger.LogWarning(
                    exception,
                    "Outbox delivery failed, will retry. Id={MessageId} Attempt={Attempt} NextAttemptAt={NextAttempt}",
                    message.Id,
                    message.AttemptCount,
                    message.NextAttemptAt);
            }
        }
    }

    /// <summary>
    /// Exponential backoff with jitter. The jitter matters: without it a batch
    /// that failed together retries together, repeatedly hammering whatever was
    /// already struggling.
    /// </summary>
    internal TimeSpan BackoffFor(int attemptCount)
    {
        double exponentialSeconds = _options.BaseRetryDelay.TotalSeconds * Math.Pow(2, attemptCount - 1);
        double cappedSeconds = Math.Min(exponentialSeconds, _options.MaxRetryDelay.TotalSeconds);
        double jitterFactor = 0.5 + Random.Shared.NextDouble(); // 0.5x to 1.5x

        double finalSeconds = Math.Min(cappedSeconds * jitterFactor, _options.MaxRetryDelay.TotalSeconds);

        return TimeSpan.FromSeconds(finalSeconds);
    }

    private static IIntegrationEvent Deserialize(OutboxMessage message)
    {
        Type? type = Type.GetType(message.PayloadType);

        if (type is null)
        {
            throw new InvalidOperationException(
                $"Outbox message {message.Id} declares payload type '{message.PayloadType}', "
                + "which cannot be resolved. The event type may have been renamed or removed.");
        }

        return JsonSerializer.Deserialize(message.Payload, type, SerializerOptions) as IIntegrationEvent
            ?? throw new InvalidOperationException(
                $"Outbox message {message.Id} did not deserialize to an integration event.");
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
