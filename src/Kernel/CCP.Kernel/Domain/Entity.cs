using CCP.Kernel.Domain;

namespace CCP.Kernel.Domain;

/// <summary>Base class for entities identified by a UUID v7.</summary>
public abstract class Entity : IEquatable<Entity>
{
    protected Entity(Guid id) => Id = id;

    /// <summary>Required by EF Core for materialization.</summary>
    protected Entity() { }

    public Guid Id { get; protected init; }

    public bool Equals(Entity? other)
        => other is not null && GetType() == other.GetType() && Id == other.Id && Id != Guid.Empty;

    public override bool Equals(object? obj) => obj is Entity entity && Equals(entity);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}

/// <summary>
/// An entity that is the entry point to a consistency boundary. Only aggregate
/// roots raise domain events, and only aggregate roots are loaded and saved as
/// a unit.
/// </summary>
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(Guid id) : base(id) { }

    protected AggregateRoot() { }

    /// <summary>Events raised by this aggregate and not yet dispatched.</summary>
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>Called by the unit of work once events have been collected.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
