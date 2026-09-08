using System.ComponentModel.DataAnnotations;

namespace CCP.Kernel.Api.Security;

/// <summary>
/// The per-minute budget for each endpoint class (ARCHITECTURE.md §12.6).
/// <para>
/// <b>Configurable, because the right number is not knowable from here.</b> It
/// depends on how many people sit behind one public address, how chatty the
/// frontend turns out to be, and what the traffic actually looks like once real
/// users arrive. A limit that cannot be adjusted without a deployment is a limit
/// that gets removed the first time it is wrong at an inconvenient hour.
/// </para>
/// <para>
/// The defaults are the values reasoned about in
/// <c>docs/security/rate-limiting.md</c> and are what a deployment gets if it
/// says nothing. Raising them is a security decision and belongs in the record,
/// which is why they are configuration rather than a constant someone edits.
/// </para>
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    /// <summary>
    /// Sign-in, refresh, password reset, MFA. The endpoints an attacker attacks.
    /// <para>
    /// Ten a minute is far more than a person mistyping a password and far less
    /// than useful for guessing.
    /// </para>
    /// </summary>
    [Range(1, 100_000)]
    public int Authentication { get; set; } = 10;

    /// <summary>
    /// Authentication attempts allowed from one source address per minute,
    /// across all accounts.
    /// <para>
    /// The other direction from <see cref="Authentication"/>: that one bounds
    /// guessing against a single account however many machines try; this one
    /// bounds a single machine working through many accounts.
    /// </para>
    /// <para>
    /// Sixty, because a whole office shares one public address and this must not
    /// become the NAT problem again — while still being far below what spraying
    /// needs to be worth doing.
    /// </para>
    /// </summary>
    [Range(1, 100_000)]
    public int AuthenticationPerAddress { get; set; } = 60;

    /// <summary>Anonymous endpoints that carry no credential, such as JWKS.</summary>
    [Range(1, 100_000)]
    public int Anonymous { get; set; } = 60;

    /// <summary>Authenticated writes, bounded by what a person can actually do.</summary>
    [Range(1, 100_000)]
    public int Write { get; set; } = 120;

    /// <summary>Authenticated reads. Generous: a busy screen makes many requests.</summary>
    [Range(1, 100_000)]
    public int Read { get; set; } = 600;
}
