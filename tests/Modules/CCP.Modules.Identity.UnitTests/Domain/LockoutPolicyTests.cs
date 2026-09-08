using CCP.Modules.Identity.Domain.Users;

namespace CCP.Modules.Identity.UnitTests.Domain;

/// <summary>
/// Progressive lockout must frustrate an attacker without becoming a weapon
/// against real users (ARCHITECTURE.md §12.4).
/// </summary>
public sealed class LockoutPolicyTests
{
    private static readonly LockoutPolicy Policy = new()
    {
        FreeAttempts = 3,
        BaseDelay = TimeSpan.FromSeconds(30),
        MaxDelay = TimeSpan.FromMinutes(30)
    };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NoDelay_WithinTheFreeAllowance(int attempts)
    {
        // Ordinary mistyping must not lock anyone out.
        Assert.Null(Policy.DelayFor(attempts));
    }

    [Theory]
    [InlineData(4, 30)]      // first past the allowance
    [InlineData(5, 60)]
    [InlineData(6, 120)]
    [InlineData(7, 240)]
    public void Delay_DoublesWithEachFurtherFailure(int attempts, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), Policy.DelayFor(attempts));
    }

    [Theory]
    [InlineData(20)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void Delay_IsCapped(int attempts)
    {
        // Capped so an account never becomes permanently unusable — a hard lock
        // is a denial-of-service tool against any user whose username is known.
        Assert.Equal(Policy.MaxDelay, Policy.DelayFor(attempts));
    }

    [Fact]
    public void Delay_DoesNotOverflowAtExtremeAttemptCounts()
    {
        // Regression guard: 2^n overflows to infinity at large n, and
        // TimeSpan.FromSeconds throws on a non-finite value. An attacker
        // controls the attempt count, so this must stay total.
        foreach (int attempts in new[] { 40, 1_000, int.MaxValue })
        {
            TimeSpan? delay = Policy.DelayFor(attempts);

            Assert.NotNull(delay);
            Assert.Equal(Policy.MaxDelay, delay);
        }
    }
}

/// <summary>Account state transitions around failed sign-ins and lockout.</summary>
public sealed class UserLockoutBehaviourTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);

    private static readonly LockoutPolicy Policy = new()
    {
        FreeAttempts = 3,
        BaseDelay = TimeSpan.FromSeconds(30),
        MaxDelay = TimeSpan.FromMinutes(30)
    };

    private static User CreateUser()
    {
        var result = User.Create("ahmad", "ahmad@example.com", "Ahmad", Now);
        Assert.True(result.IsSuccess);

        return result.Value;
    }

    [Fact]
    public void NewUser_CanAuthenticate()
    {
        Assert.True(CreateUser().CanAuthenticate(Now).IsSuccess);
    }

    [Fact]
    public void FailuresWithinAllowance_DoNotLock()
    {
        User user = CreateUser();

        for (int i = 0; i < Policy.FreeAttempts; i++)
        {
            user.RecordFailedLogin(Now, Policy);
        }

        Assert.False(user.IsLockedOut(Now));
        Assert.True(user.CanAuthenticate(Now).IsSuccess);
    }

    [Fact]
    public void FailurePastAllowance_Locks()
    {
        User user = CreateUser();

        for (int i = 0; i < Policy.FreeAttempts + 1; i++)
        {
            user.RecordFailedLogin(Now, Policy);
        }

        Assert.True(user.IsLockedOut(Now));
        Assert.Equal(UserStatus.Locked, user.Status);
        Assert.Equal(IdentityErrors.AccountLocked, user.CanAuthenticate(Now).Error);
    }

    [Fact]
    public void Lockout_LapsesOnItsOwn()
    {
        // A lapsed lockout is simply not a lockout. No background job is needed
        // to un-lock an account, which is one fewer thing to fail.
        User user = CreateUser();

        for (int i = 0; i < Policy.FreeAttempts + 1; i++)
        {
            user.RecordFailedLogin(Now, Policy);
        }

        Assert.True(user.IsLockedOut(Now));
        Assert.False(user.IsLockedOut(Now.AddMinutes(1)));
    }

    [Fact]
    public void SuccessfulLogin_ClearsFailureState()
    {
        User user = CreateUser();

        for (int i = 0; i < Policy.FreeAttempts + 1; i++)
        {
            user.RecordFailedLogin(Now, Policy);
        }

        user.RecordSuccessfulLogin(Now.AddMinutes(1));

        Assert.Equal(0, user.FailedAttemptCount);
        Assert.Null(user.LockedUntil);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal(Now.AddMinutes(1), user.LastLoginAt);
    }

    [Fact]
    public void DisabledAccount_CannotAuthenticate()
    {
        User user = CreateUser();

        Assert.True(user.Disable(Now).IsSuccess);
        Assert.Equal(IdentityErrors.AccountDisabled, user.CanAuthenticate(Now).Error);
    }

    [Fact]
    public void DisablingTwice_IsRejected()
    {
        User user = CreateUser();

        Assert.True(user.Disable(Now).IsSuccess);
        Assert.Equal(IdentityErrors.AlreadyDisabled, user.Disable(Now).Error);
    }

    [Fact]
    public void Enable_RestoresAccessAndClearsLockout()
    {
        User user = CreateUser();

        for (int i = 0; i < Policy.FreeAttempts + 1; i++)
        {
            user.RecordFailedLogin(Now, Policy);
        }

        user.Disable(Now);

        Assert.True(user.Enable(Now).IsSuccess);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal(0, user.FailedAttemptCount);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public void Unlock_ClearsLockoutWithoutTouchingTheCredential()
    {
        User user = CreateUser();

        for (int i = 0; i < Policy.FreeAttempts + 1; i++)
        {
            user.RecordFailedLogin(Now, Policy);
        }

        bool mustChangeBefore = user.MustChangePassword;

        user.Unlock(Now);

        Assert.False(user.IsLockedOut(Now));
        Assert.Equal(UserStatus.Active, user.Status);

        // Unlocking is not a password reprieve. An account that owed a password
        // change before still owes one after — otherwise an administrator could
        // clear the requirement simply by unlocking.
        Assert.Equal(mustChangeBefore, user.MustChangePassword);
    }

    [Fact]
    public void PasswordChange_ClearsLockoutAndTheChangeRequirement()
    {
        User user = CreateUser();

        Assert.True(user.MustChangePassword, "An administrator-created account must change its password.");

        for (int i = 0; i < Policy.FreeAttempts + 1; i++)
        {
            user.RecordFailedLogin(Now, Policy);
        }

        user.OnPasswordChanged(Now.AddMinutes(5));

        Assert.False(user.MustChangePassword);
        Assert.False(user.IsLockedOut(Now.AddMinutes(5)));
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal(Now.AddMinutes(5), user.PasswordChangedAt);
    }
}
