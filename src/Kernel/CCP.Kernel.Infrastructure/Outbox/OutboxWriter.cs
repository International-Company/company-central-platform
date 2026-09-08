using System.Text.Json;
using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Application.Events;
using CCP.Kernel.Domain;
using CCP.Kernel.Infrastructure.Persistence;

namespace CCP.Kernel.Infrastructure.Outbox;

/// <summary>
/// Writes integration events into the outbox table.
/// <para>
/// The row is added to the <see cref="KernelDbContext"/> change tracker but not
/// saved here: it is committed by the caller's transaction. That is the whole
/// point — the event and the change it describes succeed or fail together.
/// </para>
/// </summary>
public sealed class OutboxWriter(
    KernelDbContext dbContext,
    IRequestContext requestContext) : IOutbox
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public Task EnqueueAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        Type eventType = integrationEvent.GetType();

        var message = new OutboxMessage
        {
            Id = integrationEvent.EventId,
            EventType = integrationEvent.EventType,

            // Assembly-qualified without version, so a rebuild does not orphan
            // messages that are already queued.
            PayloadType = $"{eventType.FullName}, {eventType.Assembly.GetName().Name}",
            Payload = JsonSerializer.Serialize(integrationEvent, eventType, SerializerOptions),
            OccurredAt = integrationEvent.OccurredAt,
            CorrelationId = requestContext.CorrelationId,
            NextAttemptAt = integrationEvent.OccurredAt,
            AttemptCount = 0
        };

        dbContext.OutboxMessages.Add(message);

        return Task.CompletedTask;
    }
}
