using CCP.Kernel.Domain;

namespace CCP.Kernel.Application.Events;

/// <summary>
/// Handles an integration event relayed from the outbox.
/// <para>
/// Delivery is at-least-once, so a handler <b>must be idempotent</b>. The event
/// id is stable across redeliveries and is the natural deduplication key.
/// </para>
/// </summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Non-generic dispatch surface used by the outbox relay, which resolves
/// handlers by event type at runtime.
/// </summary>
public interface IIntegrationEventDispatcher
{
    Task DispatchAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}
