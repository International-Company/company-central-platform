namespace CCP.Modules.Identity.Domain.Users;

/// <summary>
/// Progressive lockout: the delay after a failed sign-in grows with consecutive
/// failures instead of hard-locking the account.
/// <para>
/// A permanent lock after N failures is the common design and it is a poor one:
/// it hands an attacker a denial-of-service tool against any user whose username
/// they can guess. A growing delay makes credential stuffing impractical while
/// leaving a real user only briefly inconvenienced (ARCHITECTURE.md §12.4).
/// </para>
/// <para>
/// Values are configurable so the policy can be tightened without a code change.
/// </para>
/// </summary>
public sealed class LockoutPolicy
{
    /// <summary>Failures tolerated before any delay is applied.</summary>
    public int FreeAttempts { get; init; } = 3;

    /// <summary>Delay applied at the first lockout.</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Upper bound, so an account never becomes permanently unusable.</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>The default policy, used when configuration supplies none.</summary>
    public static LockoutPolicy Default => new();

    /// <summary>
    /// The lockout duration for a given consecutive-failure count, or null when
    /// the attempt is still within the free allowance.
    /// </summary>
    public TimeSpan? DelayFor(int consecutiveFailures)
    {
        if (consecutiveFailures <= FreeAttempts)
        {
            return null;
        }

        // Doubles with each failure past the allowance: 30s, 1m, 2m, 4m … capped.
        int step = consecutiveFailures - FreeAttempts - 1;

        // Guard the exponent before it is used: at large step counts the
        // multiplication overflows to infinity, and TimeSpan.FromSeconds throws
        // on a non-finite value. Capping first keeps the method total.
        if (step >= 32)
        {
            return MaxDelay;
        }

        double seconds = BaseDelay.TotalSeconds * Math.Pow(2, step);

        return seconds >= MaxDelay.TotalSeconds
            ? MaxDelay
            : TimeSpan.FromSeconds(seconds);
    }
}
