using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;

namespace CCP.Modules.Identity.Domain.Users.Events;

/// <summary>
/// Base for identity integration events.
/// <para>
/// These cross the module boundary: Audit records them and Notifications may act
/// on them, without Identity knowing either module exists (ARCHITECTURE.md §6.3).
/// The <c>EventType</c> string is part of that contract and must not change once
/// published.
/// </para>
/// <para>
/// Events carry the username as well as the id, deliberately. An audit trail
/// that renders as "user 8f3a… was locked out" is not usable evidence once the
/// account has been renamed or deleted (ARCHITECTURE.md §15.2).
/// </para>
/// </summary>
public abstract record IdentityEvent : IIntegrationEvent
{
    protected IdentityEvent(DateTimeOffset occurredAt)
    {
        EventId = Uuid7.NewGuid(occurredAt);
        OccurredAt = occurredAt;
    }

    public Guid EventId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public abstract string EventType { get; }
}

public sealed record UserCreatedEvent(Guid UserId, string Username, string Email, DateTimeOffset At)
    : IdentityEvent(At)
{
    public override string EventType => "identity.user.created";
}

public sealed record UserDisabledEvent(Guid UserId, string Username, DateTimeOffset At)
    : IdentityEvent(At)
{
    public override string EventType => "identity.user.disabled";
}

public sealed record UserEnabledEvent(Guid UserId, string Username, DateTimeOffset At)
    : IdentityEvent(At)
{
    public override string EventType => "identity.user.enabled";
}

public sealed record UserLockedEvent(
    Guid UserId,
    string Username,
    int FailedAttemptCount,
    DateTimeOffset LockedUntil,
    DateTimeOffset At) : IdentityEvent(At)
{
    public override string EventType => "identity.user.locked";
}

public sealed record UserUnlockedEvent(Guid UserId, string Username, DateTimeOffset At)
    : IdentityEvent(At)
{
    public override string EventType => "identity.user.unlocked";
}

public sealed record UserPasswordChangedEvent(Guid UserId, string Username, DateTimeOffset At)
    : IdentityEvent(At)
{
    public override string EventType => "identity.user.password_changed";
}

public sealed record LoginSucceededEvent(
    Guid UserId,
    string Username,
    Guid SessionId,
    string? IpAddress,
    DateTimeOffset At) : IdentityEvent(At)
{
    public override string EventType => "identity.login.succeeded";
}

/// <summary>
/// Raised for every failed sign-in, including one against a username that does
/// not exist — in which case <see cref="UserId"/> is null. Recording those
/// matters: a burst of failures against unknown usernames is exactly what
/// credential stuffing looks like.
/// </summary>
public sealed record LoginFailedEvent(
    Guid? UserId,
    string AttemptedUsername,
    string Reason,
    string? IpAddress,
    DateTimeOffset At) : IdentityEvent(At)
{
    public override string EventType => "identity.login.failed";
}

public sealed record SessionRevokedEvent(
    Guid SessionId,
    Guid UserId,
    string Reason,
    DateTimeOffset At) : IdentityEvent(At)
{
    public override string EventType => "identity.session.revoked";
}

/// <summary>
/// Raised when an already-used refresh token is presented. This is the signature
/// of a stolen token and is a security incident, not a routine failure (ADR-006).
/// </summary>
public sealed record RefreshTokenReusedEvent(
    Guid UserId,
    string Username,
    Guid SessionId,
    string? IpAddress,
    DateTimeOffset At) : IdentityEvent(At)
{
    public override string EventType => "identity.refresh_token.reused";
}

/// <summary>
/// A password reset was requested.
/// <para>
/// <b>This event carries the reset token</b>, because the Notifications module
/// needs it to compose the email and has no other way to obtain it. That makes
/// the event itself sensitive: the outbox row holds a live credential until the
/// token expires. Consequences, all deliberate:
/// </para>
/// <list type="bullet">
/// <item>The token is short-lived, so the exposure window is bounded.</item>
/// <item>Audit must redact <c>Token</c> when it records this event
/// (ARCHITECTURE.md §15.7) — a reset token in an immutable audit trail would be
/// a standing account-takeover credential.</item>
/// <item>Processed outbox rows should be pruned rather than retained
/// indefinitely.</item>
/// </list>
/// </summary>
public sealed record PasswordResetRequestedEvent(
    Guid UserId,
    string Username,
    string Email,
    string Token,
    DateTimeOffset ExpiresAt,
    DateTimeOffset At) : IdentityEvent(At)
{
    public override string EventType => "identity.password_reset.requested";
}

public sealed record PasswordResetCompletedEvent(
    Guid UserId,
    string Username,
    string? IpAddress,
    DateTimeOffset At) : IdentityEvent(At)
{
    public override string EventType => "identity.password_reset.completed";
}

public sealed record UserUpdatedEvent(
    Guid UserId,
    string Username,
    DateTimeOffset At) : IdentityEvent(At)
{
    public override string EventType => "identity.user.updated";
}
