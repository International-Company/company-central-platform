using CCP.Kernel.Results;
using CCP.Modules.Identity.Domain.Users;

namespace CCP.Modules.Identity.Domain.Credentials;

/// <summary>
/// The rules a password must satisfy (ARCHITECTURE.md §12.3).
/// <para>
/// Note what is deliberately <b>absent</b>: no requirement for an uppercase
/// letter, a digit and a symbol. Composition rules of that kind are security
/// theatre — they push people toward <c>Password1!</c> and toward writing
/// passwords down, while adding very little real entropy. Current guidance
/// (NIST SP 800-63B) is to require length, screen against known-breached
/// passwords, and otherwise leave the choice alone.
/// </para>
/// <para>
/// The controls that do the work here are the minimum length, the breach check,
/// and refusing reuse.
/// </para>
/// </summary>
public sealed class PasswordPolicy
{
    /// <summary>
    /// Minimum length. Twelve rather than eight: length is the only composition
    /// property that reliably increases the cost of guessing.
    /// </summary>
    public int MinimumLength { get; init; } = 12;

    /// <summary>
    /// Maximum length. Not a security limit — it exists because Argon2id
    /// hashing cost scales with input, so an unbounded password is a cheap way
    /// to make the server do expensive work.
    /// </summary>
    public int MaximumLength { get; init; } = 256;

    /// <summary>How many previous passwords may not be reused.</summary>
    public int HistoryLength { get; init; } = 5;

    /// <summary>
    /// Whether to screen against a known-breached-password list. The single
    /// most effective check available, because credential stuffing uses exactly
    /// those passwords.
    /// </summary>
    public bool CheckBreachedPasswords { get; init; } = true;

    /// <summary>Refuse a password that contains the username.</summary>
    public bool DisallowUsernameInPassword { get; init; } = true;

    /// <summary>
    /// Optional expiry. Off by default: forced rotation makes people pick
    /// weaker, more predictable passwords, and is no longer recommended.
    /// Rotate on evidence of compromise instead.
    /// </summary>
    public TimeSpan? MaximumAge { get; init; }

    public static PasswordPolicy Default => new();

    /// <summary>
    /// Checks the rules that need no external service or stored history.
    /// Breach screening and reuse are checked by the calling use case, which
    /// has access to those.
    /// </summary>
    public Result Validate(string password, string username)
    {
        if (string.IsNullOrEmpty(password))
        {
            return Result.Failure(IdentityErrors.PasswordRequired);
        }

        List<Error> errors = [];

        if (password.Length < MinimumLength)
        {
            errors.Add(IdentityErrors.PasswordTooShort(MinimumLength));
        }

        if (password.Length > MaximumLength)
        {
            errors.Add(IdentityErrors.PasswordTooLong(MaximumLength));
        }

        if (DisallowUsernameInPassword
            && !string.IsNullOrWhiteSpace(username)
            && password.Contains(username, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(IdentityErrors.PasswordContainsUsername);
        }

        return errors.Count == 0 ? Result.Success() : Result.Failure(errors);
    }

    /// <summary>
    /// Whether a password set at <paramref name="changedAt"/> has expired.
    /// Always false when <see cref="MaximumAge"/> is unset, which is the default.
    /// </summary>
    public bool IsExpired(DateTimeOffset? changedAt, DateTimeOffset now)
        => MaximumAge is { } maximumAge
        && changedAt is { } changed
        && now - changed > maximumAge;
}
