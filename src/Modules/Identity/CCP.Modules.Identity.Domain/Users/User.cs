using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Identity.Domain.Users.Events;

namespace CCP.Modules.Identity.Domain.Users;

/// <summary>
/// One account, for one human being, across every company system
/// (ARCHITECTURE.md §7.2.1).
/// <para>
/// This is the aggregate root for identity. Business applications never create
/// their own users; they reference this one by <see cref="Entity.Id"/>, and that
/// id is stable and never reused.
/// </para>
/// <para>
/// The credential lives in a separate entity rather than on this one. Loading a
/// user to render a list should not put a password hash in memory, and the
/// separation makes it much harder for a hash to reach a DTO by accident.
/// </para>
/// </summary>
public sealed class User : AggregateRoot, IAuditableEntity
{
    private User() { }

    private User(
        Guid id,
        string username,
        string email,
        string displayName,
        UserStatus status,
        DateTimeOffset createdAt)
        : base(id)
    {
        Username = username;
        Email = email;
        DisplayName = displayName;
        Status = status;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Sign-in name, stored lower-case.
    /// <para>
    /// Normalising on write rather than comparing case-insensitively on read
    /// keeps uniqueness enforceable by an ordinary unique index, with no
    /// functional index or shadow column to keep in step. Usernames are
    /// restricted to ASCII letters, digits, dot, hyphen and underscore, so
    /// preserving the entered case carries no real value — and "Ahmad" and
    /// "ahmad" being different accounts would carry real risk.
    /// </para>
    /// </summary>
    public string Username { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public bool EmailVerified { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public UserStatus Status { get; private set; }

    /// <summary>
    /// Forces a password change at next sign-in. Set when an administrator
    /// creates or resets an account, so an administrator-chosen password is
    /// never a lasting credential.
    /// </summary>
    public bool MustChangePassword { get; private set; }

    /// <summary>
    /// When the current lockout expires. Null when not locked. Kept separate
    /// from <see cref="Status"/> so the lockout can lapse without a background
    /// job having to clear it.
    /// </summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    /// <summary>
    /// Consecutive failed sign-in attempts. Reset on success, and the input to
    /// the progressive lockout delay.
    /// </summary>
    public int FailedAttemptCount { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public DateTimeOffset? PasswordChangedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    // -----------------------------------------------------------------------
    // Creation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates an account. Shape is validated here; uniqueness is a database
    /// concern and is checked by the calling use case.
    /// </summary>
    public static Result<User> Create(
        string username,
        string email,
        string displayName,
        DateTimeOffset now,
        bool mustChangePassword = true)
    {
        Result validation = ValidateUsername(username)
            .Combine(ValidateEmail(email))
            .Combine(ValidateDisplayName(displayName));

        if (validation.IsFailure)
        {
            return Result.Failure<User>(validation.Errors);
        }

        var user = new User(
            Uuid7.NewGuid(now),
            username.Trim().ToLowerInvariant(),
            email.Trim().ToLowerInvariant(),
            displayName.Trim(),
            UserStatus.Active,
            now)
        {
            MustChangePassword = mustChangePassword
        };

        user.Raise(new UserCreatedEvent(user.Id, user.Username, user.Email, now));

        return user;
    }

    // -----------------------------------------------------------------------
    // Authentication decisions
    // -----------------------------------------------------------------------

    /// <summary>
    /// Whether this account may sign in at <paramref name="now"/>.
    /// <para>
    /// Returns a domain error rather than a boolean so the caller knows *why*.
    /// The API deliberately does not pass that reason to an anonymous caller —
    /// it is recorded in the audit trail and in security events instead
    /// (ARCHITECTURE.md §7.2.1).
    /// </para>
    /// </summary>
    public Result CanAuthenticate(DateTimeOffset now)
    {
        if (Status == UserStatus.Disabled)
        {
            return Result.Failure(IdentityErrors.AccountDisabled);
        }

        if (Status == UserStatus.PendingActivation)
        {
            return Result.Failure(IdentityErrors.AccountNotActivated);
        }

        if (IsLockedOut(now))
        {
            return Result.Failure(IdentityErrors.AccountLocked);
        }

        return Result.Success();
    }

    /// <summary>
    /// True while a lockout is still in force. A lapsed lockout is simply not a
    /// lockout — no cleanup job is needed to un-lock an account.
    /// </summary>
    public bool IsLockedOut(DateTimeOffset now)
        => LockedUntil is { } until && until > now;

    /// <summary>
    /// Records a failed sign-in and applies progressive lockout.
    /// <para>
    /// The delay grows with consecutive failures rather than hard-locking the
    /// account, deliberately: a hard lock hands an attacker a denial-of-service
    /// tool against any user whose username they know (ARCHITECTURE.md §12.4).
    /// </para>
    /// </summary>
    public void RecordFailedLogin(DateTimeOffset now, LockoutPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        FailedAttemptCount++;

        TimeSpan? delay = policy.DelayFor(FailedAttemptCount);

        if (delay is { } lockoutDuration)
        {
            LockedUntil = now.Add(lockoutDuration);
            Status = UserStatus.Locked;

            Raise(new UserLockedEvent(Id, Username, FailedAttemptCount, LockedUntil.Value, now));
        }
    }

    /// <summary>Records a successful sign-in and clears failure state.</summary>
    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        FailedAttemptCount = 0;
        LockedUntil = null;
        LastLoginAt = now;

        if (Status == UserStatus.Locked)
        {
            Status = UserStatus.Active;
        }
    }

    // -----------------------------------------------------------------------
    // Administration
    // -----------------------------------------------------------------------

    public Result Disable(DateTimeOffset now)
    {
        if (Status == UserStatus.Disabled)
        {
            return Result.Failure(IdentityErrors.AlreadyDisabled);
        }

        Status = UserStatus.Disabled;
        Raise(new UserDisabledEvent(Id, Username, now));

        return Result.Success();
    }

    public Result Enable(DateTimeOffset now)
    {
        if (Status == UserStatus.Active)
        {
            return Result.Failure(IdentityErrors.AlreadyActive);
        }

        Status = UserStatus.Active;
        FailedAttemptCount = 0;
        LockedUntil = null;

        Raise(new UserEnabledEvent(Id, Username, now));

        return Result.Success();
    }

    /// <summary>
    /// Clears a lockout administratively, without changing the credential.
    /// Used when a legitimate user has locked themselves out.
    /// </summary>
    public void Unlock(DateTimeOffset now)
    {
        FailedAttemptCount = 0;
        LockedUntil = null;

        if (Status == UserStatus.Locked)
        {
            Status = UserStatus.Active;
        }

        Raise(new UserUnlockedEvent(Id, Username, now));
    }

    public Result ChangeEmail(string email, DateTimeOffset now)
    {
        Result validation = ValidateEmail(email);

        if (validation.IsFailure)
        {
            return validation;
        }

        string normalized = email.Trim().ToLowerInvariant();

        if (string.Equals(Email, normalized, StringComparison.Ordinal))
        {
            return Result.Success();
        }

        Email = normalized;

        // A changed address is unverified until proven, otherwise changing the
        // address would be a way to bypass verification entirely.
        EmailVerified = false;
        UpdatedAt = now;

        return Result.Success();
    }

    public Result ChangeDisplayName(string displayName, DateTimeOffset now)
    {
        Result validation = ValidateDisplayName(displayName);

        if (validation.IsFailure)
        {
            return validation;
        }

        DisplayName = displayName.Trim();
        UpdatedAt = now;

        return Result.Success();
    }

    public void MarkEmailVerified(DateTimeOffset now)
    {
        EmailVerified = true;
        UpdatedAt = now;
    }

    /// <summary>
    /// Records that the credential changed. The hash itself belongs to
    /// <see cref="Credentials.UserCredential"/>; this only tracks the fact.
    /// </summary>
    public void OnPasswordChanged(DateTimeOffset now)
    {
        PasswordChangedAt = now;
        MustChangePassword = false;
        FailedAttemptCount = 0;
        LockedUntil = null;

        if (Status == UserStatus.Locked)
        {
            Status = UserStatus.Active;
        }

        Raise(new UserPasswordChangedEvent(Id, Username, now));
    }

    /// <summary>Requires a password change at next sign-in.</summary>
    public void RequirePasswordChange() => MustChangePassword = true;

    // -----------------------------------------------------------------------
    // Validation
    // -----------------------------------------------------------------------

    private static Result ValidateUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return Result.Failure(IdentityErrors.UsernameRequired);
        }

        string trimmed = username.Trim();

        if (trimmed.Length is < 3 or > 64)
        {
            return Result.Failure(IdentityErrors.UsernameLength);
        }

        // An allow-list, so anything not plainly safe is rejected. Usernames end
        // up in logs, audit records and URLs.
        foreach (char c in trimmed)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_'))
            {
                return Result.Failure(IdentityErrors.UsernameCharacters);
            }
        }

        return Result.Success();
    }

    private static Result ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Result.Failure(IdentityErrors.EmailRequired);
        }

        string trimmed = email.Trim();

        if (trimmed.Length > 256)
        {
            return Result.Failure(IdentityErrors.EmailTooLong);
        }

        // Deliberately shallow. Full RFC 5322 validation rejects valid
        // addresses and accepts undeliverable ones; the only real proof that an
        // address works is sending to it, which verification does.
        int at = trimmed.IndexOf('@', StringComparison.Ordinal);

        bool shaped = at > 0
            && at < trimmed.Length - 1
            && trimmed.IndexOf('@', at + 1) < 0
            && trimmed.LastIndexOf('.') > at
            && !trimmed.Contains(' ', StringComparison.Ordinal);

        return shaped ? Result.Success() : Result.Failure(IdentityErrors.EmailInvalid);
    }

    private static Result ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Result.Failure(IdentityErrors.DisplayNameRequired);
        }

        return displayName.Trim().Length > 128
            ? Result.Failure(IdentityErrors.DisplayNameTooLong)
            : Result.Success();
    }
}
