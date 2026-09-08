using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;

namespace CCP.Modules.Organization.Domain.Units.Events;

/// <summary>
/// Base for Organization integration events (ARCHITECTURE.md §6.3).
/// <para>
/// Events carry the human-readable code and name alongside the id, for the same
/// reason Identity's carry the username: an audit trail reading "unit 8f3a… was
/// moved" is not usable evidence once the unit has been renamed or deactivated.
/// </para>
/// </summary>
public abstract record OrganizationEvent : IIntegrationEvent
{
    protected OrganizationEvent(DateTimeOffset occurredAt)
    {
        EventId = Uuid7.NewGuid(occurredAt);
        OccurredAt = occurredAt;
    }

    public Guid EventId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public abstract string EventType { get; }
}

public sealed record OrganizationUnitCreatedEvent(
    Guid UnitId,
    string Code,
    string Name,
    Guid? ParentId,
    DateTimeOffset At) : OrganizationEvent(At)
{
    public override string EventType => "organization.unit.created";
}

/// <summary>
/// A unit moved to a new parent.
/// <para>
/// <b>Consumers must treat this as invalidating cached organizational scope.</b>
/// From Phase 4, authorization caches which units a user's grant covers; a move
/// changes that for the moved unit and every descendant, and a stale cache would
/// grant access to the wrong part of the company (ARCHITECTURE.md §7.2.2).
/// </para>
/// </summary>
public sealed record OrganizationUnitMovedEvent(
    Guid UnitId,
    string Code,
    Guid? NewParentId,
    string OldPath,
    string NewPath,
    DateTimeOffset At) : OrganizationEvent(At)
{
    public override string EventType => "organization.unit.moved";
}

public sealed record OrganizationUnitDeactivatedEvent(
    Guid UnitId,
    string Code,
    DateTimeOffset At) : OrganizationEvent(At)
{
    public override string EventType => "organization.unit.deactivated";
}

public sealed record EmployeeHiredEvent(
    Guid EmployeeId,
    string EmployeeNumber,
    string Name,
    Guid UnitId,
    Guid? UserId,
    DateTimeOffset At) : OrganizationEvent(At)
{
    public override string EventType => "organization.employee.hired";
}

/// <summary>
/// An employee changed unit, position or manager.
/// <para>
/// Also a scope-invalidating event: a user's <c>Unit</c> grant follows their
/// employee record, so a transfer moves what they can see.
/// </para>
/// </summary>
public sealed record EmployeeTransferredEvent(
    Guid EmployeeId,
    string EmployeeNumber,
    Guid? FromUnitId,
    Guid ToUnitId,
    Guid? UserId,
    DateTimeOffset At) : OrganizationEvent(At)
{
    public override string EventType => "organization.employee.transferred";
}

public sealed record EmployeeDeactivatedEvent(
    Guid EmployeeId,
    string EmployeeNumber,
    Guid? UserId,
    DateTimeOffset At) : OrganizationEvent(At)
{
    public override string EventType => "organization.employee.deactivated";
}

public sealed record ManagerChangedEvent(
    Guid EmployeeId,
    string EmployeeNumber,
    Guid? OldManagerId,
    Guid? NewManagerId,
    DateTimeOffset At) : OrganizationEvent(At)
{
    public override string EventType => "organization.employee.manager_changed";
}

/// <summary>
/// An employee record was linked to, or unlinked from, a Platform user account.
/// <para>
/// Consumed by anything that maps a signed-in user to their place in the
/// organization — which from Phase 4 is authorization itself.
/// </para>
/// </summary>
public sealed record EmployeeUserLinkChangedEvent(
    Guid EmployeeId,
    string EmployeeNumber,
    Guid? OldUserId,
    Guid? NewUserId,
    DateTimeOffset At) : OrganizationEvent(At)
{
    public override string EventType => "organization.employee.user_link_changed";
}
