using CCP.Kernel.Application.Abstractions;
using CCP.Modules.Identity.Application.Abstractions;
using CCP.Modules.Identity.Domain.Credentials;
using CCP.Modules.Identity.Domain.Sessions;
using CCP.Modules.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CCP.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of the Identity module's persistence port.
/// <para>
/// It also serves as the module's unit of work: <see cref="SaveChangesAsync"/>
/// commits the entity changes and the outbox rows written during the same
/// operation in one transaction, which is what makes an event impossible to
/// lose after a successful write, or to record after a rollback
/// (ARCHITECTURE.md §8.5).
/// </para>
/// </summary>
public sealed class IdentityRepository(IdentityDbContext dbContext) : IIdentityRepository, IUnitOfWork
{
    // --- Users -------------------------------------------------------------

    public Task<User?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public Task<User?> FindUserByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        // Usernames are stored lower-case by the domain, so normalising the
        // input here makes this an ordinary indexed equality lookup rather than
        // a scan with a function applied to every row.
        string normalized = username.Trim().ToLowerInvariant();

        return dbContext.Users.FirstOrDefaultAsync(u => u.Username == normalized, cancellationToken);
    }

    public Task<User?> FindUserByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        string normalized = email.Trim().ToLowerInvariant();

        return dbContext.Users.FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken);
    }

    public Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken = default)
    {
        string normalized = username.Trim().ToLowerInvariant();

        return dbContext.Users.AnyAsync(u => u.Username == normalized, cancellationToken);
    }

    public Task<bool> EmailExistsAsync(
        string email,
        Guid? excludingUserId = null,
        CancellationToken cancellationToken = default)
    {
        string normalized = email.Trim().ToLowerInvariant();

        return dbContext.Users.AnyAsync(
            u => u.Email == normalized && (excludingUserId == null || u.Id != excludingUserId),
            cancellationToken);
    }

    public void AddUser(User user) => dbContext.Users.Add(user);

    // --- Credentials -------------------------------------------------------

    public Task<UserCredential?> FindCredentialAsync(Guid userId, CancellationToken cancellationToken = default)
        => dbContext.Credentials.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

    public void AddCredential(UserCredential credential) => dbContext.Credentials.Add(credential);

    public async Task<IReadOnlyList<PasswordHistoryEntry>> GetPasswordHistoryAsync(
        Guid userId,
        int count,
        CancellationToken cancellationToken = default)
        => await dbContext.PasswordHistory
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.CreatedAt)
            .Take(count)
            .ToListAsync(cancellationToken);

    public void AddPasswordHistory(PasswordHistoryEntry entry) => dbContext.PasswordHistory.Add(entry);

    public async Task PrunePasswordHistoryAsync(
        Guid userId,
        int keep,
        CancellationToken cancellationToken = default)
    {
        // Password history is a control and a liability at once: it must be long
        // enough to prevent reuse and no longer, so entries beyond the retained
        // count are removed rather than accumulating indefinitely.
        List<PasswordHistoryEntry> stale = await dbContext.PasswordHistory
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.CreatedAt)
            .Skip(keep)
            .ToListAsync(cancellationToken);

        if (stale.Count > 0)
        {
            dbContext.PasswordHistory.RemoveRange(stale);
        }
    }

    // --- Password reset ----------------------------------------------------

    public Task<PasswordResetToken?> FindPasswordResetTokenByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
        => dbContext.PasswordResetTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<PasswordResetToken>> GetActivePasswordResetTokensAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
        => await dbContext.PasswordResetTokens
            .Where(t => t.UserId == userId && t.UsedAt == null && t.InvalidatedAt == null)
            .ToListAsync(cancellationToken);

    public void AddPasswordResetToken(PasswordResetToken token)
        => dbContext.PasswordResetTokens.Add(token);

    // --- Sessions ----------------------------------------------------------

    public Task<Session?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => dbContext.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

    public async Task<IReadOnlyList<Session>> GetActiveSessionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
        => await dbContext.Sessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .OrderByDescending(s => s.LastActivityAt)
            .ToListAsync(cancellationToken);

    public void AddSession(Session session) => dbContext.Sessions.Add(session);

    // --- Refresh tokens ----------------------------------------------------

    public Task<RefreshToken?> FindRefreshTokenByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
        => dbContext.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> GetTokenFamilyAsync(
        Guid familyId,
        CancellationToken cancellationToken = default)
        => await dbContext.RefreshTokens
            .Where(t => t.FamilyId == familyId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> GetSessionRefreshTokensAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
        => await dbContext.RefreshTokens
            .Where(t => t.SessionId == sessionId)
            .ToListAsync(cancellationToken);

    public void AddRefreshToken(RefreshToken token) => dbContext.RefreshTokens.Add(token);

    // --- Login history -----------------------------------------------------

    public void AddLoginAttempt(LoginAttempt attempt) => dbContext.LoginAttempts.Add(attempt);

    public async Task<IReadOnlyList<LoginAttempt>> GetRecentLoginAttemptsAsync(
        Guid userId,
        int count,
        CancellationToken cancellationToken = default)
        => await dbContext.LoginAttempts
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.OccurredAt)
            .Take(count)
            .ToListAsync(cancellationToken);

    // --- Queries -----------------------------------------------------------

    public async Task<(IReadOnlyList<User> Items, long TotalCount)> SearchUsersAsync(
        string? searchTerm,
        UserStatus? status,
        int skip,
        int take,
        string? sortField,
        bool sortDescending,
        CancellationToken cancellationToken = default)
    {
        IQueryable<User> query = dbContext.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            // PostgreSQL ILIKE rather than ToLower().Contains(): the
            // StringComparison overloads C# would prefer are not translatable by
            // EF Core, and ToLower() applies a function to every row. ILIKE
            // expresses case-insensitive matching directly in SQL.
            //
            // The leading wildcard means no B-tree index can serve this. That is
            // acceptable for an administrative user list of this size; if it ever
            // is not, the fix is a trigram (pg_trgm) index, noted for Phase 17.
            string pattern = $"%{EscapeLikePattern(searchTerm.Trim())}%";

            query = query.Where(u =>
                EF.Functions.ILike(u.Username, pattern)
                || EF.Functions.ILike(u.Email, pattern)
                || EF.Functions.ILike(u.DisplayName, pattern));
        }

        if (status is { } requiredStatus)
        {
            query = query.Where(u => u.Status == requiredStatus);
        }

        long total = await query.LongCountAsync(cancellationToken);

        // Sort fields are allow-listed by the caller before reaching here
        // (ADR-008), so this switch is exhaustive without risk.
        query = (sortField, sortDescending) switch
        {
            ("username", false) => query.OrderBy(u => u.Username),
            ("username", true) => query.OrderByDescending(u => u.Username),
            ("displayName", false) => query.OrderBy(u => u.DisplayName),
            ("displayName", true) => query.OrderByDescending(u => u.DisplayName),
            ("lastLoginAt", false) => query.OrderBy(u => u.LastLoginAt),
            ("lastLoginAt", true) => query.OrderByDescending(u => u.LastLoginAt),
            (_, true) => query.OrderByDescending(u => u.CreatedAt),
            _ => query.OrderBy(u => u.CreatedAt)
        };

        List<User> items = await query.Skip(skip).Take(take).ToListAsync(cancellationToken);

        return (items, total);
    }

    /// <summary>
    /// Escapes the LIKE metacharacters in a user-supplied search term.
    /// <para>
    /// Without this, a term containing <c>%</c> matches everything and one
    /// containing <c>_</c> matches any character — not an injection, since the
    /// value is still parameterised, but a confusing result and a needless way
    /// to make the database scan more than it should.
    /// </para>
    /// </summary>
    private static string EscapeLikePattern(string term)
        => term.Replace("\\", "\\\\", StringComparison.Ordinal)
               .Replace("%", "\\%", StringComparison.Ordinal)
               .Replace("_", "\\_", StringComparison.Ordinal);

    // --- Unit of work ------------------------------------------------------

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => dbContext.SaveChangesAsync(cancellationToken);
}
