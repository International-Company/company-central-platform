using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;

namespace CCP.Modules.Authorization.Domain.Applications;

/// <summary>
/// A client secret an application signs in with.
/// <para>
/// <b>An application may hold more than one at a time, and that is the whole
/// point.</b> Rotation without downtime means the new secret works before the
/// old one stops: the owning team deploys the new one at their own pace and
/// revokes the old one when their fleet has caught up. A model with exactly one
/// live secret makes every rotation an outage, which is how secrets end up never
/// being rotated.
/// </para>
/// <para>
/// The secret is stored hashed and shown once, at issuance. There is no path
/// that reads it back — not for an administrator, not for the owning team, not
/// for support. A secret that can be retrieved is a secret that will be
/// retrieved by whoever compromises the console.
/// </para>
/// </summary>
public sealed class ApplicationCredential : AggregateRoot, IAuditableEntity
{
    /// <summary>
    /// How many live credentials one application may hold.
    /// <para>
    /// Two is the number a rotation needs. Allowing more turns "we are mid
    /// rotation" into "nobody knows how many keys exist", which is the state
    /// this limit is here to prevent.
    /// </para>
    /// </summary>
    public const int MaxLiveCredentials = 2;

    private ApplicationCredential() { }

    private ApplicationCredential(
        Guid id,
        Guid applicationId,
        string clientId,
        string secretHash,
        string label,
        DateTimeOffset now,
        DateTimeOffset? expiresAt)
        : base(id)
    {
        ApplicationId = applicationId;
        ClientId = clientId;
        SecretHash = secretHash;
        Label = label;
        ExpiresAt = expiresAt;
        CreatedAt = now;
    }

    public Guid ApplicationId { get; private set; }

    /// <summary>
    /// The public half. Safe to log, safe to put in a configuration file, and
    /// useless on its own — which is what makes rate limiting and attribution by
    /// client id reasonable.
    /// </summary>
    public string ClientId { get; private set; } = string.Empty;

    /// <summary>
    /// A hash of the secret. See <c>ApplicationSecretHasher</c> for why this is
    /// SHA-256 and not Argon2, which is not the answer anybody expects.
    /// </summary>
    public string SecretHash { get; private set; } = string.Empty;

    /// <summary>
    /// What this credential is for, in the owning team's words — "production",
    /// "the nightly reconciliation job". Rotation is much less frightening when
    /// the thing being revoked has a name.
    /// </summary>
    public string Label { get; private set; } = string.Empty;

    /// <summary>
    /// When it stops working on its own, or null for indefinite.
    /// <para>
    /// An expiry is a rotation somebody has already agreed to. Indefinite is
    /// allowed because forcing one on a system nobody maintains produces an
    /// outage at 3am rather than a rotation.
    /// </para>
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? RevokedBy { get; private set; }

    /// <summary>
    /// The last time this credential was exchanged for a token.
    /// <para>
    /// The single most useful field here. "Can we revoke the old one?" is
    /// answered by whether anything has used it this week, and without this the
    /// answer is a guess and the rotation never finishes.
    /// </para>
    /// </summary>
    public DateTimeOffset? LastUsedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>Whether this credential may be exchanged for a token now.</summary>
    public bool IsLive(DateTimeOffset now) =>
        RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);

    public static Result<ApplicationCredential> Issue(
        Guid applicationId,
        string clientId,
        string secretHash,
        string label,
        DateTimeOffset now,
        DateTimeOffset? expiresAt)
    {
        if (applicationId == Guid.Empty)
        {
            return Result.Failure<ApplicationCredential>(AuthorizationErrors.ApplicationNotFound);
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            return Result.Failure<ApplicationCredential>(AuthorizationErrors.CredentialLabelRequired);
        }

        if (expiresAt is { } expiry && expiry <= now)
        {
            return Result.Failure<ApplicationCredential>(AuthorizationErrors.ExpiryInThePast);
        }

        return Result.Success(new ApplicationCredential(
            Uuid7.NewGuid(now), applicationId, clientId, secretHash, label.Trim(), now, expiresAt));
    }

    /// <summary>
    /// Stops this credential working. Immediately, and with no grace period.
    /// <para>
    /// The whole value of revocation is that it is instant. Anything else — a
    /// cache, a delay, a "next refresh" — turns "we think this key leaked" into
    /// an interval during which the leaked key still works.
    /// </para>
    /// <para>
    /// Tokens already issued from it are a separate matter and are bounded by
    /// their own short lifetime; see the machine-token documentation.
    /// </para>
    /// </summary>
    public Result Revoke(Guid revokedBy, DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return Result.Failure(AuthorizationErrors.CredentialAlreadyRevoked);
        }

        RevokedAt = now;
        RevokedBy = revokedBy;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>Records a successful exchange. Called on the token path.</summary>
    public void RecordUse(DateTimeOffset now) => LastUsedAt = now;
}
