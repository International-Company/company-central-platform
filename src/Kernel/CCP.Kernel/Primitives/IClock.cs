namespace CCP.Kernel.Primitives;

/// <summary>
/// The source of the current time.
/// <para>
/// Nothing in the Platform calls <c>DateTimeOffset.UtcNow</c> directly. Time is
/// injected so that expiry, lockout, token lifetime and SLA behaviour can be
/// tested without waiting for real time to pass.
/// </para>
/// </summary>
public interface IClock
{
    /// <summary>The current instant, always in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>The real system clock. Replaced in tests.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
