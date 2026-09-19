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

// ---------------------------------------------------------------------------
// Passkeys
// ---------------------------------------------------------------------------

/// <summary>
/// What the browser needs in order to create a passkey, in the shape
/// <c>navigator.credentials.create</c> expects once the caller has turned the
/// base64url strings back into bytes.
/// </summary>
public sealed record PasskeyRegistrationOptionsDto(
    string Challenge,
    string RelyingPartyId,
    string RelyingPartyName,

    /// <summary>The account, as the authenticator will remember it.</summary>
    string UserId,
    string Username,
    string DisplayName,

    /// <summary>
    /// COSE algorithm identifiers this Platform can verify, best first. Sent
    /// rather than assumed: an authenticator picks the first it supports, and
    /// one that picked an algorithm the Platform cannot check would produce a
    /// passkey that fails at every sign-in.
    /// </summary>
    IReadOnlyList<int> Algorithms,

    /// <summary>
    /// Credentials this account already has. The authenticator refuses to make
    /// a second one for itself, so somebody adding a passkey twice from the
    /// same device is told by their own device rather than by a conflict from
    /// the server.
    /// </summary>
    IReadOnlyList<string> ExcludeCredentials,

    int TimeoutMilliseconds);

/// <summary>
/// What the browser needs in order to sign in with a passkey.
/// <para>
/// No list of credentials, and no username was asked for. The authenticator
/// finds the passkey itself, so an anonymous caller learns nothing about which
/// accounts exist or which of them have one.
/// </para>
/// </summary>
public sealed record PasskeySignInOptionsDto(
    string Challenge,
    string RelyingPartyId,
    int TimeoutMilliseconds);

/// <summary>One passkey on somebody's account, as their security screen shows it.</summary>
public sealed record PasskeyDto(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt);

