using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;

namespace CCP.Modules.Security.Contracts.Events;

/// <summary>
/// Base for the Security module's integration events.
/// <para>
/// Distinct from <c>SecurityEvent</c>, the module's own log of what is being
/// attempted, which never leaves the module. These are the published shapes
/// another module reads, and the <c>EventType</c> string is part of that
/// contract: it must not change once published.
/// </para>
/// <para>
/// <b>Security had none until a notification needed one.</b> A template telling
/// somebody that two-factor authentication was turned on for their account was
/// seeded in both languages, and nothing could ever send it, because nothing
/// outside this module learned that an enrolment happened.
/// </para>
/// </summary>
public abstract record SecurityIntegrationEvent : IIntegrationEvent
{
    protected SecurityIntegrationEvent(DateTimeOffset occurredAt)
    {
        EventId = Uuid7.NewGuid(occurredAt);
        OccurredAt = occurredAt;
    }

    public Guid EventId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public abstract string EventType { get; }
}

/// <summary>
/// A second factor was turned on for an account.
/// <para>
/// The notification this carries is the one that exposes a takeover. Somebody
/// holding a stolen session enrols an authenticator of their own so that they
/// keep access after the password is changed; the account's real owner is the
/// person who needs to hear that it happened, and this is how they do.
/// </para>
/// </summary>
public sealed record MfaEnrolledEvent(Guid UserId, string Username, DateTimeOffset At)
    : SecurityIntegrationEvent(At)
{
    public override string EventType => "security.mfa.enrolled";
}
