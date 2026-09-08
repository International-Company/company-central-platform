using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;

namespace CCP.Modules.Security.Domain.Mfa;

/// <summary>
/// A record that a user proved their second factor at a moment in time, on a
/// particular session.
/// <para>
/// <b>Bound to the session, not just the user.</b> A confirmation on one device
/// must not privilege another — otherwise an administrator confirming on their
/// laptop would silently hand step-up to whoever holds a stolen token from a
/// different browser, which is the exact case step-up exists to catch.
/// </para>
/// <para>
/// <b>Held in the database rather than in the token.</b> Putting a step-up claim
/// in the access token would mean it could not be revoked before the token
/// expired, and revoking it promptly is most of its value: disabling MFA, a
/// changed password, or a signed-out session should all end elevation at once.
/// It also keeps the browser out of it — a claim the client holds is a claim the
/// client can be tricked into replaying.
/// </para>
/// </summary>
public sealed class StepUpConfirmation : Entity
{
    private StepUpConfirmation() { }

    private StepUpConfirmation(
        Guid id, Guid userId, Guid sessionId, DateTimeOffset confirmedAt, DateTimeOffset expiresAt)
        : base(id)
    {
        UserId = userId;
        SessionId = sessionId;
        ConfirmedAt = confirmedAt;
        ExpiresAt = expiresAt;
    }

    public Guid UserId { get; private set; }

    /// <summary>The session on which the factor was proven.</summary>
    public Guid SessionId { get; private set; }

    public DateTimeOffset ConfirmedAt { get; private set; }

    /// <summary>
    /// When elevation lapses. Absolute rather than sliding: a sliding window
    /// would keep a walked-away-from session elevated indefinitely as long as it
    /// kept working, which is the opposite of the intent.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>
    /// When elevation was revoked ahead of expiry, if it was. Set when the
    /// factor itself changes — disabling MFA must not leave a live elevation
    /// behind it.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsValidAt(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    public static StepUpConfirmation Issue(
        Guid userId, Guid sessionId, DateTimeOffset now, TimeSpan validity)
        => new(Uuid7.NewGuid(), userId, sessionId, now, now.Add(validity));

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
