using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Identity.Domain.Users;

namespace CCP.Modules.Identity.Domain.Credentials;

/// <summary>
/// A passkey: one public key, held by one authenticator, for one account.
/// <para>
/// This is what makes signing in with a fingerprint possible, and the most
/// important thing about it is what it does <b>not</b> contain. No fingerprint,
/// no face, no biometric of any kind reaches this Platform or leaves the
/// device. The sensor unlocks a private key that the device holds and never
/// surrenders; the Platform stores only the matching public key, and sign-in is
/// the device proving it still has the private one. A database taken whole
/// yields nothing anybody can sign in with.
/// </para>
/// <para>
/// <b>Stronger than a password, not merely more convenient.</b> The key is
/// bound to this Platform's domain by the browser, so a convincing copy of the
/// sign-in page on another domain cannot ask for it — phishing, which is how
/// most real accounts are lost, stops working. There is no shared secret to
/// reuse on another site, to breach, or to read over somebody's shoulder.
/// </para>
/// <para>
/// <b>It counts as both factors.</b> The credential lives in hardware the
/// person holds, and the Platform accepts an assertion only when the
/// authenticator says it verified the human first. Possession and inherence, in
/// one gesture: signing in this way is not followed by a request for a code.
/// </para>
/// </summary>
public sealed class WebAuthnCredential : Entity
{
    private WebAuthnCredential() { }

    private WebAuthnCredential(
        Guid id,
        Guid userId,
        string credentialId,
        byte[] publicKey,
        int algorithm,
        uint signCount,
        Guid? authenticatorGuid,
        bool userVerified,
        string name,
        DateTimeOffset createdAt)
        : base(id)
    {
        UserId = userId;
        CredentialId = credentialId;
        PublicKey = publicKey;
        Algorithm = algorithm;
        SignCount = signCount;
        AuthenticatorGuid = authenticatorGuid;
        UserVerified = userVerified;
        Name = name;
        CreatedAt = createdAt;
    }

    public Guid UserId { get; private set; }

    /// <summary>
    /// The authenticator's identifier for this credential, base64url as the
    /// browser sends it.
    /// <para>
    /// Unique across the Platform, and not only per user: an authenticator that
    /// produced this id will produce it again, so the same physical key
    /// registered against a second account would otherwise create two rows that
    /// a sign-in cannot choose between.
    /// </para>
    /// </summary>
    public string CredentialId { get; private set; } = string.Empty;

    /// <summary>
    /// The public key, in the COSE encoding the authenticator produced. Public
    /// by definition: it verifies a signature and creates none.
    /// </summary>
    public byte[] PublicKey { get; private set; } = [];

    /// <summary>
    /// The COSE algorithm identifier the key is for: -7 for ES256, -257 for
    /// RS256. Stored rather than inferred, so a key is always verified with the
    /// algorithm it was registered with.
    /// </summary>
    public int Algorithm { get; private set; }

    /// <summary>
    /// The authenticator's own counter at the last accepted assertion.
    /// <para>
    /// Its purpose is to expose a cloned authenticator: a genuine one increments
    /// on every signature, so a counter that goes backwards means two devices
    /// hold the same key. Authenticators that keep no counter report zero for
    /// ever, which is allowed and means only that this particular check cannot
    /// speak — most phones and laptops are in that group.
    /// </para>
    /// </summary>
    public uint SignCount { get; private set; }

    /// <summary>
    /// Which model of authenticator this is, as the device reported it. Null
    /// when it declined to say, which is common and deliberate: it is an
    /// identifier shared by every unit of a model, and a device that withholds
    /// it is protecting its owner from being told apart.
    /// </summary>
    public Guid? AuthenticatorGuid { get; private set; }

    /// <summary>
    /// Whether the authenticator verified the person at registration, by
    /// fingerprint, face or device passcode.
    /// <para>
    /// Recorded because it decides what the credential is worth. One that only
    /// proves possession of a device is a single factor; one that verified a
    /// human is two, and only the second may stand in for a password and a code
    /// together.
    /// </para>
    /// </summary>
    public bool UserVerified { get; private set; }

    /// <summary>
    /// What the person calls this device. Their own words: a list of
    /// indistinguishable entries is a list nobody can safely remove anything
    /// from, and removing the wrong one locks somebody out of their own account.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    /// <summary>
    /// When the credential was removed. Kept rather than deleted: which device
    /// could sign in to an account, and when it stopped being able to, is part
    /// of the security record.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    public static WebAuthnCredential Register(
        Guid userId,
        string credentialId,
        byte[] publicKey,
        int algorithm,
        uint signCount,
        Guid? authenticatorGuid,
        bool userVerified,
        string name,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialId);
        ArgumentNullException.ThrowIfNull(publicKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new WebAuthnCredential(
            Uuid7.NewGuid(now),
            userId,
            credentialId,
            publicKey,
            algorithm,
            signCount,
            authenticatorGuid,
            userVerified,
            name.Trim(),
            now);
    }

    /// <summary>
    /// Accepts an assertion from this credential and moves the counter forward.
    /// <para>
    /// Refuses a counter that has not advanced, unless the authenticator keeps
    /// none. That is the cloned-authenticator check, and it is the one place a
    /// stolen key can be caught after the fact.
    /// </para>
    /// </summary>
    public Result RecordUse(uint presentedSignCount, DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return Result.Failure(IdentityErrors.PasskeyRevoked);
        }

        // Zero from both sides means the authenticator does not count. Zero from
        // one side only is still not evidence of anything, because an
        // authenticator may begin counting at any point.
        if (presentedSignCount != 0 && SignCount != 0 && presentedSignCount <= SignCount)
        {
            return Result.Failure(IdentityErrors.PasskeyCounterWentBackwards);
        }

        if (presentedSignCount > SignCount)
        {
            SignCount = presentedSignCount;
        }

        LastUsedAt = now;

        return Result.Success();
    }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
    }

    public void Revoke(DateTimeOffset now)
    {
        RevokedAt ??= now;
    }
}
