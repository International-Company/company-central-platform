using CCP.Modules.Identity.Domain.Credentials;
using CCP.Modules.Identity.Domain.Sessions;
using CCP.Modules.Identity.Domain.Users;

namespace CCP.Modules.Identity.Application.Abstractions;

/// <summary>
/// Persistence for the Identity module, expressed as the Application layer needs
/// it rather than as the database offers it (ARCHITECTURE.md §6.1).
/// </summary>
public interface IIdentityRepository
{
    // --- Users -------------------------------------------------------------

    Task<User?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds by username, case-insensitively. Backed by a functional unique
    /// index on lower(username) (ARCHITECTURE.md §10.5).
    /// </summary>
    Task<User?> FindUserByUsernameAsync(string username, CancellationToken cancellationToken = default);

    Task<User?> FindUserByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken = default);

    Task<bool> EmailExistsAsync(string email, Guid? excludingUserId = null, CancellationToken cancellationToken = default);

    void AddUser(User user);

    // --- Credentials -------------------------------------------------------

    Task<UserCredential?> FindCredentialAsync(Guid userId, CancellationToken cancellationToken = default);

    void AddCredential(UserCredential credential);

    Task<IReadOnlyList<PasswordHistoryEntry>> GetPasswordHistoryAsync(
        Guid userId, int count, CancellationToken cancellationToken = default);

    void AddPasswordHistory(PasswordHistoryEntry entry);

    /// <summary>Removes history entries beyond the retained count.</summary>
    Task PrunePasswordHistoryAsync(Guid userId, int keep, CancellationToken cancellationToken = default);

    // --- Password reset ----------------------------------------------------

    Task<PasswordResetToken?> FindPasswordResetTokenByHashAsync(
        string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tokens for a user that have not been spent or invalidated. Loaded when a
    /// new one is issued and when the password changes by another route, so an
    /// outstanding link cannot outlive the password it was meant to replace.
    /// </summary>
    Task<IReadOnlyList<PasswordResetToken>> GetActivePasswordResetTokensAsync(
        Guid userId, CancellationToken cancellationToken = default);

    void AddPasswordResetToken(PasswordResetToken token);

    // --- Sessions ----------------------------------------------------------

    Task<Session?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Session>> GetActiveSessionsAsync(Guid userId, CancellationToken cancellationToken = default);

    void AddSession(Session session);

    // --- Refresh tokens ----------------------------------------------------

    Task<RefreshToken?> FindRefreshTokenByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every token in a family. Loaded when reuse is detected so the whole
    /// family can be revoked at once (ADR-006).
    /// </summary>
    Task<IReadOnlyList<RefreshToken>> GetTokenFamilyAsync(Guid familyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every refresh token belonging to a session. Used at sign-out: revoking
    /// only the token last presented would leave earlier, unspent siblings
    /// valid.
    /// </summary>
    Task<IReadOnlyList<RefreshToken>> GetSessionRefreshTokensAsync(
        Guid sessionId, CancellationToken cancellationToken = default);

    void AddRefreshToken(RefreshToken token);

    // --- Login history -----------------------------------------------------

    void AddLoginAttempt(LoginAttempt attempt);

    Task<IReadOnlyList<LoginAttempt>> GetRecentLoginAttemptsAsync(
        Guid userId, int count, CancellationToken cancellationToken = default);

    // --- Queries -----------------------------------------------------------

    /// <summary>
    /// One page of users, filtered and sorted.
    /// <para>
    /// Returns entities rather than a projection so the handler owns the mapping
    /// to a DTO. That mapping is the boundary keeping internal fields out of a
    /// response, and it is hand-written for exactly that reason.
    /// </para>
    /// </summary>
    Task<(IReadOnlyList<User> Items, long TotalCount)> SearchUsersAsync(
        string? searchTerm,
        UserStatus? status,
        int skip,
        int take,
        string? sortField,
        bool sortDescending,
        CancellationToken cancellationToken = default);
}
