using System.Security.Claims;

namespace CCP.Modules.Identity.Application.Abstractions;

/// <summary>Issues and validates access tokens (ADR-006).</summary>
public interface ITokenService
{
    /// <summary>
    /// Issues a short-lived signed access token.
    /// <para>
    /// Claims are kept minimal. Permissions are deliberately not embedded here
    /// in Phase 2 — the Authorization module arrives in Phase 4 and decides how
    /// they travel (ADR-007 §14.4).
    /// </para>
    /// </summary>
    string CreateAccessToken(Guid userId, string username, Guid sessionId, DateTimeOffset now);

    /// <summary>Lifetime of an issued access token, for the response body.</summary>
    TimeSpan AccessTokenLifetime { get; }

    /// <summary>
    /// Validates a token and returns its principal, or null when invalid.
    /// Used by tests and by internal introspection; ordinary request
    /// authentication goes through the framework's JWT handler.
    /// </summary>
    ClaimsPrincipal? ValidateAccessToken(string token);
}

/// <summary>
/// Generates and hashes refresh tokens.
/// <para>
/// Separate from <see cref="ITokenService"/> because refresh tokens are opaque
/// random values rather than signed documents, and are stored hashed. They have
/// nothing in common with a JWT beyond both being credentials.
/// </para>
/// </summary>
public interface IRefreshTokenGenerator
{
    /// <summary>
    /// Creates a cryptographically random token and returns both the value to
    /// hand the caller and the hash to store. The plaintext is never persisted.
    /// </summary>
    (string Token, string Hash) Generate();

    /// <summary>Hashes a presented token so it can be looked up.</summary>
    string HashToken(string token);
}
