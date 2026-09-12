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

/// <summary>
/// Sees every integration event, whatever its type.
/// <para>
/// <b>The seam a typed handler cannot serve.</b> A handler subscribes to one
/// event; an observer is for the thing that has to be offered <i>all</i> of
/// them without naming any — today that is outbound webhooks, where which
/// events matter is chosen by a business application at runtime and cannot be
/// known when the Platform is compiled.
/// </para>
/// <para>
/// Deliberately narrow, and deliberately not a way to subscribe. An observer
/// cannot influence the event, and a module that wanted to <i>act</i> on one
/// registers a handler for it by name, where the dependency is visible.
/// </para>
/// <para>
/// Runs after the typed handlers and under the same at-least-once guarantee, so
/// an observer must be idempotent. Throwing fails the whole message and every
/// handler runs again.
/// </para>
/// </summary>
public interface IIntegrationEventObserver
{
    Task ObserveAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}
