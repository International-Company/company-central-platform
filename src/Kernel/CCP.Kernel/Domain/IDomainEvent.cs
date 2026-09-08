namespace CCP.Kernel.Domain;

/// <summary>
/// Something that happened inside a module, raised by an entity and dispatched
/// after the transaction commits. Domain events stay within their module.
/// </summary>
public interface IDomainEvent
{
    /// <summary>Unique identity of this event occurrence, used for idempotency.</summary>
    Guid EventId { get; }

    /// <summary>When the event occurred, in UTC.</summary>
    DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// A domain event that crosses a module boundary. Integration events are
/// written to the outbox in the same transaction as the change that produced
/// them, then relayed asynchronously (ARCHITECTURE.md §8.5).
/// <para>
/// This is the mechanism that lets Audit record everything while depending on
/// nothing: it subscribes rather than being called.
/// </para>
/// </summary>
public interface IIntegrationEvent : IDomainEvent
{
    /// <summary>
    /// Stable event type name used for routing and stored in the outbox, e.g.
    /// <c>identity.user.created</c>. It is part of the contract between
    /// modules, so it must not change once published.
    /// </summary>
    string EventType { get; }
}
