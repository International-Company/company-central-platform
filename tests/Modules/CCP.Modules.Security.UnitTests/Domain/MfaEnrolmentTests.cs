using CCP.Kernel.Results;
using CCP.Modules.Security.Domain.Mfa;

namespace CCP.Modules.Security.UnitTests.Domain;

/// <summary>
/// The enrolment lifecycle and the rules that make a second factor worth having.
/// </summary>
public sealed class MfaEnrolmentTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private static MfaEnrolment NewEnrolment()
        => MfaEnrolment.Begin(Guid.NewGuid(), "encrypted", Now);

    [Fact]
    public void Begin_LeavesTheEnrolmentPending()
    {
        MfaEnrolment enrolment = NewEnrolment();

        Assert.Equal(MfaEnrolmentStatus.Pending, enrolment.Status);

        // The important half: a pending enrolment is not a factor. If it counted,
        // a user whose scan failed would be locked out by a secret they cannot
        // produce a code from.
        Assert.False(enrolment.IsActive);
        Assert.Null(enrolment.ActivatedAt);
    }

    [Fact]
    public void Activate_MakesTheFactorCount()
    {
        MfaEnrolment enrolment = NewEnrolment();

        Result result = enrolment.Activate(Now);

        Assert.True(result.IsSuccess);
        Assert.True(enrolment.IsActive);
        Assert.Equal(Now, enrolment.ActivatedAt);
    }

    [Fact]
    public void Activate_RefusesWhenAlreadyActive()
    {
        MfaEnrolment enrolment = NewEnrolment();
        enrolment.Activate(Now);

        Result result = enrolment.Activate(Now.AddMinutes(1));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Activate_ClearsFailuresAccumulatedDuringEnrolment()
    {
        MfaEnrolment enrolment = NewEnrolment();

        enrolment.RecordFailure();
        enrolment.RecordFailure();

        enrolment.Activate(Now);

        // Failures while getting the scan right must not count against the
        // factor once it works, or a fiddly enrolment would arrive
        // pre-exhausted.
        Assert.Equal(0, enrolment.FailedAttempts);
    }

    [Fact]
    public void LockedOut_OnlyAfterTheAttemptLimit()
    {
        MfaEnrolment enrolment = NewEnrolment();
        enrolment.Activate(Now);

        for (int i = 0; i < MfaEnrolment.MaxFailedAttempts - 1; i++)
        {
            enrolment.RecordFailure();
            Assert.False(enrolment.IsLockedOut);
        }

        enrolment.RecordFailure();

        Assert.True(enrolment.IsLockedOut);
    }

    [Fact]
    public void RecordSuccess_ResetsTheFailureCount()
    {
        MfaEnrolment enrolment = NewEnrolment();
        enrolment.Activate(Now);

        enrolment.RecordFailure();
        enrolment.RecordFailure();
        enrolment.RecordSuccess(Now.AddMinutes(5));

        Assert.Equal(0, enrolment.FailedAttempts);
        Assert.Equal(Now.AddMinutes(5), enrolment.LastUsedAt);
    }

    [Fact]
    public void RedeemRecoveryCode_SpendsItExactlyOnce()
    {
        MfaEnrolment enrolment = NewEnrolment();
        enrolment.Activate(Now);
        enrolment.ReplaceRecoveryCodes(["hash-a", "hash-b"], Now);

        RecoveryCode? first = enrolment.RedeemRecoveryCode("hash-a", Now);
        RecoveryCode? second = enrolment.RedeemRecoveryCode("hash-a", Now.AddMinutes(1));

        Assert.NotNull(first);

        // A recovery code that worked twice would be a password with none of a
        // password's protections.
        Assert.Null(second);
        Assert.Equal(1, enrolment.RemainingRecoveryCodes);
    }

    [Fact]
    public void RedeemRecoveryCode_ReturnsNullForAnUnknownHash()
    {
        MfaEnrolment enrolment = NewEnrolment();
        enrolment.Activate(Now);
        enrolment.ReplaceRecoveryCodes(["hash-a"], Now);

        Assert.Null(enrolment.RedeemRecoveryCode("not-a-real-hash", Now));
        Assert.Equal(1, enrolment.RemainingRecoveryCodes);
    }

    [Fact]
    public void ReplaceRecoveryCodes_RetiresTheOldSet()
    {
        MfaEnrolment enrolment = NewEnrolment();
        enrolment.Activate(Now);
        enrolment.ReplaceRecoveryCodes(["old-a", "old-b"], Now);

        enrolment.ReplaceRecoveryCodes(["new-a"], Now.AddDays(1));

        // A printout from a year ago must stop working the moment the user
        // believes they have regenerated it.
        Assert.Null(enrolment.RedeemRecoveryCode("old-a", Now.AddDays(1)));
        Assert.NotNull(enrolment.RedeemRecoveryCode("new-a", Now.AddDays(1)));
    }

    [Fact]
    public void Disable_InvalidatesEveryRecoveryCode()
    {
        MfaEnrolment enrolment = NewEnrolment();
        enrolment.Activate(Now);
        enrolment.ReplaceRecoveryCodes(["hash-a", "hash-b"], Now);

        enrolment.Disable(Now.AddHours(1));

        Assert.Equal(MfaEnrolmentStatus.Disabled, enrolment.Status);
        Assert.Equal(0, enrolment.RemainingRecoveryCodes);

        // A code outliving the factor it belonged to would be a standing bypass
        // for an account that no longer expects one.
        Assert.Null(enrolment.RedeemRecoveryCode("hash-a", Now.AddHours(2)));
    }

    [Fact]
    public void Disable_RefusesWhenAlreadyDisabled()
    {
        MfaEnrolment enrolment = NewEnrolment();
        enrolment.Activate(Now);
        enrolment.Disable(Now.AddHours(1));

        Assert.True(enrolment.Disable(Now.AddHours(2)).IsFailure);
    }

    [Fact]
    public void RewrapSecret_ReplacesTheCiphertext()
    {
        MfaEnrolment enrolment = NewEnrolment();
        enrolment.Activate(Now);

        enrolment.RewrapSecret("encrypted-under-the-new-key");

        Assert.Equal("encrypted-under-the-new-key", enrolment.EncryptedSecret);
    }

    /// <summary>
    /// Re-encrypting is housekeeping, and housekeeping is not a use of the
    /// factor.
    /// <para>
    /// <c>LastUsedAt</c> answers "when did this person last prove their second
    /// factor". Moving it here would put an entry in somebody's security history
    /// for something they did not do, and would make a dormant factor look
    /// exercised on the day of a rotation.
    /// </para>
    /// </summary>
    [Fact]
    public void RewrapSecret_DoesNotCountAsUsingTheFactor()
    {
        MfaEnrolment enrolment = NewEnrolment();
        enrolment.Activate(Now);
        enrolment.RecordSuccess(Now);

        enrolment.RewrapSecret("encrypted-under-the-new-key");

        Assert.Equal(Now, enrolment.LastUsedAt);
    }

    /// <summary>
    /// A disabled enrolment keeps whatever it had. Rewriting a secret nobody can
    /// use would be work done to preserve something already gone — and it would
    /// quietly carry a dead factor forward onto every future key.
    /// </summary>
    [Fact]
    public void RewrapSecret_RefusedOnADisabledEnrolment()
    {
        MfaEnrolment enrolment = NewEnrolment();
        enrolment.Activate(Now);
        enrolment.Disable(Now.AddHours(1));

        enrolment.RewrapSecret("encrypted-under-the-new-key");

        Assert.Equal("encrypted", enrolment.EncryptedSecret);
    }

    /// <summary>
    /// And nothing is never an improvement on something. A protector that failed
    /// to produce a value must not be able to erase a working secret through
    /// this door.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RewrapSecret_RefusesToReplaceASecretWithNothing(string replacement)
    {
        MfaEnrolment enrolment = NewEnrolment();
        enrolment.Activate(Now);

        enrolment.RewrapSecret(replacement);

        Assert.Equal("encrypted", enrolment.EncryptedSecret);
    }
}
