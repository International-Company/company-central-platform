using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;

namespace CCP.Modules.Security.Domain.Mfa;

/// <summary>Where an enrolment is in its lifecycle.</summary>
public enum MfaEnrolmentStatus
{
    /// <summary>
    /// A secret has been issued but not yet proven. The user has scanned the QR
    /// code and not yet entered a code from it.
    /// <para>
    /// Pending enrolments do <b>not</b> count as MFA. Treating them as active
    /// would let someone start enrolment, never finish, and be locked out of
    /// their own account by a factor they cannot produce.
    /// </para>
    /// </summary>
    Pending = 1,

    /// <summary>Proven and in force.</summary>
    Active = 2,

    /// <summary>Turned off. Kept for the record rather than deleted.</summary>
    Disabled = 3
}

/// <summary>
/// A user's second authentication factor.
/// <para>
/// The TOTP secret must be <b>recoverable</b> to verify a code, so unlike a
/// password it cannot be hashed — it is encrypted at rest with a key from the
/// secret manager. That is a meaningfully weaker position than password storage,
/// and it is why the key lives outside the database: a leaked backup then yields
/// ciphertext rather than working second factors.
/// </para>
/// </summary>
public sealed class MfaEnrolment : AggregateRoot
{
    private readonly List<RecoveryCode> _recoveryCodes = [];

    private MfaEnrolment() { }

    private MfaEnrolment(Guid id, Guid userId, string encryptedSecret, DateTimeOffset now)
        : base(id)
    {
        UserId = userId;
        EncryptedSecret = encryptedSecret;
        Status = MfaEnrolmentStatus.Pending;
        CreatedAt = now;
    }

    public Guid UserId { get; private set; }

    /// <summary>
    /// The TOTP secret, encrypted. Never returned by any endpoint after
    /// enrolment begins — the user has it in their authenticator app, and
    /// re-showing it would let anyone with a stolen session clone the factor.
    /// </summary>
    public string EncryptedSecret { get; private set; } = string.Empty;

    /// <summary>
    /// Replaces the stored ciphertext with the same secret under a different
    /// key.
    /// <para>
    /// <b>The secret does not change; only the key protecting it does.</b> That
    /// is why this is not an enrolment and raises nothing: nobody has proved
    /// anything new, no factor has been added or removed, and an audit entry
    /// here would be a line about housekeeping in a trail people read to find
    /// out who did what.
    /// </para>
    /// <para>
    /// Refused on a disabled enrolment. Rewriting a secret nobody can use would
    /// be work done to preserve something already gone.
    /// </para>
    /// <para>
    /// It takes no timestamp and moves none. <c>LastUsedAt</c> answers "when did
    /// this person last prove their factor", and housekeeping is not a use —
    /// touching it here would put a line in somebody's security history for
    /// something they did not do.
    /// </para>
    /// </summary>
    public void RewrapSecret(string encryptedSecret)
    {
        if (string.IsNullOrWhiteSpace(encryptedSecret) || DisabledAt is not null)
        {
            return;
        }

        EncryptedSecret = encryptedSecret;
    }

    public MfaEnrolmentStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public DateTimeOffset? DisabledAt { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    /// <summary>
    /// Failed verification attempts since the last success. Bounded so that
    /// guessing a six-digit code is impractical even within the drift window.
    /// </summary>
    public int FailedAttempts { get; private set; }

    public IReadOnlyList<RecoveryCode> RecoveryCodes => _recoveryCodes.AsReadOnly();

    public bool IsActive => Status == MfaEnrolmentStatus.Active;

    /// <summary>
    /// Attempts allowed before verification is refused outright.
    /// <para>
    /// A TOTP code is six digits and three periods are accepted at any instant,
    /// so roughly three in a million guesses succeed. Ten attempts keeps that
    /// negligible while tolerating a user whose phone clock is wrong.
    /// </para>
    /// </summary>
    public const int MaxFailedAttempts = 10;

    /// <summary>Begins enrolment with a freshly generated, encrypted secret.</summary>
    public static MfaEnrolment Begin(Guid userId, string encryptedSecret, DateTimeOffset now)
        => new(Uuid7.NewGuid(now), userId, encryptedSecret, now);

    /// <summary>
    /// Activates the enrolment once the user has proven they hold the secret.
    /// <para>
    /// Proof is required before the factor counts. Activating on issue would
    /// lock out anyone whose scan failed or whose phone clock is wrong, and they
    /// would have no way back in.
    /// </para>
    /// </summary>
    public Result Activate(DateTimeOffset now)
    {
        if (Status == MfaEnrolmentStatus.Active)
        {
            return Result.Failure(SecurityErrors.MfaAlreadyActive);
        }

        Status = MfaEnrolmentStatus.Active;
        ActivatedAt = now;
        FailedAttempts = 0;

        return Result.Success();
    }

    /// <summary>
    /// Records a successful verification.
    /// </summary>
    public void RecordSuccess(DateTimeOffset now)
    {
        LastUsedAt = now;
        FailedAttempts = 0;
    }

    /// <summary>Records a failed verification.</summary>
    public void RecordFailure() => FailedAttempts++;

    /// <summary>
    /// Whether too many failures have accumulated to keep trying.
    /// </summary>
    public bool IsLockedOut => FailedAttempts >= MaxFailedAttempts;

    /// <summary>
    /// Turns the factor off.
    /// <para>
    /// Recovery codes are invalidated with it. A code that outlived the factor
    /// it belonged to would be a standing bypass for an account that no longer
    /// expects one.
    /// </para>
    /// </summary>
    public Result Disable(DateTimeOffset now)
    {
        if (Status == MfaEnrolmentStatus.Disabled)
        {
            return Result.Failure(SecurityErrors.MfaNotActive);
        }

        Status = MfaEnrolmentStatus.Disabled;
        DisabledAt = now;

        foreach (RecoveryCode code in _recoveryCodes)
        {
            code.Invalidate(now);
        }

        return Result.Success();
    }

    /// <summary>
    /// Replaces the recovery codes with a new set.
    /// <para>
    /// Replaces rather than adds: issuing a new set must retire the old one, or
    /// a printout from a year ago keeps working long after the user believed
    /// they had regenerated it.
    /// </para>
    /// </summary>
    public void ReplaceRecoveryCodes(IReadOnlyList<string> hashedCodes, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(hashedCodes);

        foreach (RecoveryCode existing in _recoveryCodes)
        {
            existing.Invalidate(now);
        }

        _recoveryCodes.RemoveAll(c => c.IsSpent);

        foreach (string hash in hashedCodes)
        {
            _recoveryCodes.Add(RecoveryCode.Create(Id, hash, now));
        }
    }

    /// <summary>
    /// Finds a usable recovery code matching the given hash and spends it.
    /// <para>
    /// Single use. A recovery code that worked twice would be a password with
    /// none of a password's protections.
    /// </para>
    /// </summary>
    public RecoveryCode? RedeemRecoveryCode(string hash, DateTimeOffset now)
    {
        RecoveryCode? match = _recoveryCodes.FirstOrDefault(
            c => c.IsUsable && string.Equals(c.CodeHash, hash, StringComparison.Ordinal));

        match?.Redeem(now);

        return match;
    }

    /// <summary>How many recovery codes remain, so the user can be warned.</summary>
    public int RemainingRecoveryCodes => _recoveryCodes.Count(c => c.IsUsable);
}

/// <summary>
/// A one-time code that substitutes for the second factor.
/// <para>
/// Stored hashed, shown once, single use. The user has lost their phone; these
/// are what stops that being permanent — and they are full authentication
/// bypasses, so they get the same treatment as any other credential.
/// </para>
/// </summary>
public sealed class RecoveryCode : Entity
{
    private RecoveryCode() { }

    private RecoveryCode(Guid id, Guid enrolmentId, string codeHash, DateTimeOffset now)
        : base(id)
    {
        EnrolmentId = enrolmentId;
        CodeHash = codeHash;
        CreatedAt = now;
    }

    public Guid EnrolmentId { get; private set; }

    /// <summary>SHA-256 of the code. The code itself is never stored.</summary>
    public string CodeHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RedeemedAt { get; private set; }

    public DateTimeOffset? InvalidatedAt { get; private set; }

    public bool IsSpent => RedeemedAt is not null || InvalidatedAt is not null;

    public bool IsUsable => !IsSpent;

    internal static RecoveryCode Create(Guid enrolmentId, string codeHash, DateTimeOffset now)
        => new(Uuid7.NewGuid(now), enrolmentId, codeHash, now);

    internal void Redeem(DateTimeOffset now) => RedeemedAt ??= now;

    internal void Invalidate(DateTimeOffset now)
    {
        if (RedeemedAt is null)
        {
            InvalidatedAt ??= now;
        }
    }
}
