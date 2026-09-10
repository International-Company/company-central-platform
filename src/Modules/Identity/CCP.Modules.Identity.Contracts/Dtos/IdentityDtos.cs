namespace CCP.Modules.Identity.Contracts.Dtos;

/// <summary>
/// A user, as other modules and API callers see them.
/// <para>
/// Hand-written rather than mapped automatically, and deliberately so: this type
/// is the boundary that keeps a password hash from reaching a response. An
/// automatic mapper adds fields when the entity gains them, silently. This does
/// not.
/// </para>
/// </summary>
/// <param name="PreferredLocale">
/// The language this person chose, or null if they never chose one. Null is a
/// real answer: it means the company default applies, and keeps applying if the
/// company later changes it.
/// </param>
public sealed record UserDto(
    Guid Id,
    string Username,
    string Email,
    bool EmailVerified,
    string DisplayName,
    string Status,
    bool MustChangePassword,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    string? PreferredLocale);

/// <summary>The signed-in user's own profile.</summary>
public sealed record CurrentUserDto(
    Guid Id,
    string Username,
    string Email,
    string DisplayName,
    bool MustChangePassword,
    Guid SessionId);

/// <summary>
/// One of the caller's sessions, for the "where am I signed in" view.
/// <para>
/// Carries no token or token hash — only enough to recognise a session and
/// decide whether to end it.
/// </para>
/// </summary>
public sealed record SessionDto(
    Guid Id,
    string? IpAddress,
    string? UserAgent,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    DateTimeOffset AbsoluteExpiresAt,
    bool IsCurrent);

/// <summary>One sign-in attempt in the user's own history.</summary>
public sealed record LoginAttemptDto(
    bool Succeeded,
    string? IpAddress,
    string? UserAgent,
    DateTimeOffset OccurredAt);

/// <summary>
/// The result of a successful authentication.
/// <para>
/// The refresh token appears here because the BFF, running server-side, stores
/// it. It is never written to browser-accessible storage (ADR-006 §13.2).
/// </para>
/// </summary>
public sealed record AuthenticationResultDto(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    string TokenType,
    CurrentUserDto User);
