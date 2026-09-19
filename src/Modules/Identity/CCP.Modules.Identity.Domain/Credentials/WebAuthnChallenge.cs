using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;

namespace CCP.Modules.Identity.Domain.Credentials;

/// <summary>What a challenge was issued for. A challenge for one is not valid for the other.</summary>
public enum WebAuthnCeremony
{
    /// <summary>Adding a passkey to an account somebody is already signed in to.</summary>
    Registration = 1,

    /// <summary>Signing in with a passkey already registered.</summary>
    Authentication = 2,
}

/// <summary>
/// A one-time random value the authenticator must sign, so that a signature
/// proves the device is here now.
/// <para>
/// <b>Held by the server, and spent.</b> The whole security of a passkey rests
/// on this: without a challenge the server chose, an attacker who once observed
/// a valid signature could replay it for ever. Two mistakes are common enough
/// to name, and both are prevented here rather than in a handler that might
/// forget.
/// </para>
/// <list type="number">
/// <item>
/// <b>Accepting the challenge the client sends back as the challenge.</b> That
/// is not a check; it is the attacker choosing the question. The row is found
/// by its value and must exist, unspent and unexpired, on this Platform.
/// </item>
/// <item>
/// <b>Allowing it to be used twice.</b> A challenge is consumed the moment it
/// is redeemed, whether the ceremony then succeeds or fails, so a captured
/// exchange cannot be replayed even once.
/// </item>
/// </list>
/// <para>
/// Short-lived as well, because a challenge nobody has answered in a few
/// minutes is one nobody is going to.
/// </para>
/// </summary>
public sealed class WebAuthnChallenge : Entity
{
    private WebAuthnChallenge() { }

    private WebAuthnChallenge(
        Guid id,
        string value,
        WebAuthnCeremony ceremony,
        Guid? userId,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
        : base(id)
    {
        Value = value;
        Ceremony = ceremony;
        UserId = userId;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>The random value, base64url, exactly as it is sent to the browser.</summary>
    public string Value { get; private set; } = string.Empty;

    public WebAuthnCeremony Ceremony { get; private set; }

    /// <summary>
    /// Who it was issued to, when that is known.
    /// <para>
    /// Null for sign-in, and deliberately: the browser finds the passkey itself
    /// and the Platform learns who it belongs to only from the answer. Asking
    /// for a username first would tell an anonymous caller which usernames have
    /// passkeys, which is the account enumeration this Platform refuses
    /// everywhere else.
    /// </para>
    /// </summary>
    public Guid? UserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When it was spent. Non-null means it can never be used again.</summary>
    public DateTimeOffset? RedeemedAt { get; private set; }

    public static WebAuthnChallenge Issue(
        string value,
        WebAuthnCeremony ceremony,
        Guid? userId,
        DateTimeOffset now,
        TimeSpan lifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return new WebAuthnChallenge(Uuid7.NewGuid(now), value, ceremony, userId, now, now.Add(lifetime));
    }

    public bool IsUsableAt(DateTimeOffset now, WebAuthnCeremony ceremony)
        => RedeemedAt is null && now < ExpiresAt && Ceremony == ceremony;

    /// <summary>
    /// Spends the challenge.
    /// <para>
    /// Called before the signature is checked, not after. A challenge left
    /// unspent because verification failed is a challenge an attacker can keep
    /// trying against, and trying repeatedly is exactly what they would do.
    /// </para>
    /// </summary>
    public void Redeem(DateTimeOffset now) => RedeemedAt ??= now;
}
