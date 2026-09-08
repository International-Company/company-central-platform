using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;

namespace CCP.Modules.Authorization.Domain.Roles.Events;

/// <summary>
/// Base for Authorization integration events.
/// <para>
/// Every one of these is security-relevant. Audit records them all, and the
/// human-readable code travels alongside the id for the same reason it does
/// elsewhere: "role 8f3a… was granted" is not usable evidence.
/// </para>
/// </summary>
public abstract record AuthorizationEvent : IIntegrationEvent
{
    protected AuthorizationEvent(DateTimeOffset occurredAt)
    {
        EventId = Uuid7.NewGuid(occurredAt);
        OccurredAt = occurredAt;
    }

    public Guid EventId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public abstract string EventType { get; }
}

public sealed record RoleCreatedEvent(
    Guid RoleId,
    string Code,
    string Name,
    DateTimeOffset At) : AuthorizationEvent(At)
{
    public override string EventType => "authz.role.created";
}

/// <summary>
/// A permission was added to a role.
/// <para>
/// This silently widens access for <b>every</b> holder of the role, which makes
/// it one of the highest-impact changes anyone can make in the Platform. It is
/// audited with the permission named, not just its id.
/// </para>
/// </summary>
public sealed record RolePermissionGrantedEvent(
    Guid RoleId,
    string RoleCode,
    Guid PermissionId,
    string PermissionName,
    DateTimeOffset At) : AuthorizationEvent(At)
{
    public override string EventType => "authz.role.permission_granted";
}

public sealed record RolePermissionRevokedEvent(
    Guid RoleId,
    string RoleCode,
    Guid PermissionId,
    string PermissionName,
    DateTimeOffset At) : AuthorizationEvent(At)
{
    public override string EventType => "authz.role.permission_revoked";
}

public sealed record RoleGrantedToUserEvent(
    Guid AssignmentId,
    Guid UserId,
    Guid RoleId,
    string RoleCode,
    string ScopeType,
    Guid? ScopeUnitId,
    Guid GrantedBy,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset At) : AuthorizationEvent(At)
{
    public override string EventType => "authz.role.granted";
}

public sealed record RoleRevokedFromUserEvent(
    Guid AssignmentId,
    Guid UserId,
    Guid RoleId,
    string RoleCode,
    Guid RevokedBy,
    DateTimeOffset At) : AuthorizationEvent(At)
{
    public override string EventType => "authz.role.revoked";
}

public sealed record ApplicationRegisteredEvent(
    Guid ApplicationId,
    string Code,
    string Name,
    DateTimeOffset At) : AuthorizationEvent(At)
{
    public override string EventType => "authz.application.registered";
}

/// <summary>
/// An application declared its permissions.
/// <para>
/// The mechanism by which a system the Platform has never heard of becomes a
/// first-class consumer (ADR-012). Recorded so that "when did this permission
/// start existing" has an answer.
/// </para>
/// </summary>
public sealed record PermissionsDeclaredEvent(
    Guid ApplicationId,
    string ApplicationCode,
    int DeclaredCount,
    int DeactivatedCount,
    DateTimeOffset At) : AuthorizationEvent(At)
{
    public override string EventType => "authz.application.permissions_declared";
}

/// <summary>
/// A denied authorization decision on a sensitive resource.
/// <para>
/// Raised deliberately: repeated denials are reconnaissance
/// (ARCHITECTURE.md §14.5). One is noise; a burst against many permissions from
/// one caller is someone mapping what they can reach.
/// </para>
/// </summary>
public sealed record AccessDeniedEvent(
    Guid? UserId,
    string? Username,
    string PermissionName,
    string? IpAddress,
    DateTimeOffset At) : AuthorizationEvent(At)
{
    public override string EventType => "authz.access.denied";
}
