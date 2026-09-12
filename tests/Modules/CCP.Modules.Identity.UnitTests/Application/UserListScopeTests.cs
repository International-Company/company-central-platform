using CCP.Kernel.Paging;
using CCP.Kernel.Results;
using CCP.Modules.Identity.Application.Abstractions;
using CCP.Modules.Identity.Application.Users;
using CCP.Modules.Identity.Contracts.Dtos;
using CCP.Modules.Identity.Domain.Credentials;
using CCP.Modules.Identity.Domain.Sessions;
using CCP.Modules.Identity.Domain.Users;

namespace CCP.Modules.Identity.UnitTests.Application;

/// <summary>
/// What a scope means when the thing being listed is a user account.
/// <para>
/// <b>It meant nothing at all until now</b> (debt #15): a caller with
/// <c>platform.users.view</c> at any scope saw every account in the company,
/// because Identity has no organizational dimension of its own and nobody had
/// decided what a department-scoped user list should contain.
/// </para>
/// <para>
/// The decision: a user account has no department, but the person behind it does
/// — so a department-scoped caller sees the accounts of the people in their
/// department. The cases below are the two that a filter written carelessly gets
/// backwards, and both fail open rather than closed.
/// </para>
/// </summary>
public sealed class UserListScopeTests
{
    /// <summary>
    /// The whole company means no filter at all, and nothing is asked of the
    /// organization.
    /// </summary>
    [Fact]
    public async Task AnUnrestrictedCallerSeesEverybody()
    {
        var placement = new RecordingPlacement([Guid.CreateVersion7()]);
        var repository = new RecordingRepository();

        await new SearchUsersHandler(repository, placement).HandleAsync(
            Query(restricted: false, prefixes: []));

        Assert.Null(repository.VisibleUserIds);
        Assert.False(placement.WasAsked);
    }

    [Fact]
    public async Task ARestrictedCallerSeesOnlyThePeopleUnderTheirUnits()
    {
        Guid inTheDepartment = Guid.CreateVersion7();

        var placement = new RecordingPlacement([inTheDepartment]);
        var repository = new RecordingRepository();

        await new SearchUsersHandler(repository, placement).HandleAsync(
            Query(restricted: true, prefixes: ["/finance/"]));

        Assert.Equal(["/finance/"], placement.AskedFor);
        Assert.Equal([inTheDepartment], repository.VisibleUserIds);
    }

    /// <summary>
    /// The assertion this class exists for.
    /// <para>
    /// A restricted caller whose scope resolves to no units sees <b>nobody</b>.
    /// Treating an empty set of prefixes as "no filter" is the natural mistake
    /// and the expensive one: it turns a scope that grants nothing into a scope
    /// that grants the whole company, and every other test here would still
    /// pass.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ARestrictedCallerWithNoUnitsSeesNobody()
    {
        var placement = new RecordingPlacement([]);
        var repository = new RecordingRepository();

        await new SearchUsersHandler(repository, placement).HandleAsync(
            Query(restricted: true, prefixes: []));

        Assert.NotNull(repository.VisibleUserIds);
        Assert.Empty(repository.VisibleUserIds);
    }

    // --- Fixtures -----------------------------------------------------------

    private static SearchUsersQuery Query(bool restricted, string[] prefixes)
        => new(PageRequest.Create(1, 25, null).Value, null, null, prefixes, restricted);

    private sealed class RecordingPlacement(Guid[] under) : IUserPlacement
    {
        public bool WasAsked { get; private set; }

        public IReadOnlyCollection<string>? AskedFor { get; private set; }

        public Task<IReadOnlyList<Guid>> GetUserIdsUnderAsync(
            IReadOnlyCollection<string> unitPathPrefixes,
            CancellationToken cancellationToken = default)
        {
            WasAsked = true;
            AskedFor = unitPathPrefixes;

            return Task.FromResult<IReadOnlyList<Guid>>(under);
        }
    }

    /// <summary>
    /// Records what the search was narrowed to and refuses every other question,
    /// so the handler cannot quietly start depending on something else.
    /// </summary>
    private sealed class RecordingRepository : IIdentityRepository
    {
        public IReadOnlyCollection<Guid>? VisibleUserIds { get; private set; }

        public Task<(IReadOnlyList<User> Items, long TotalCount)> SearchUsersAsync(
            string? searchTerm,
            UserStatus? status,
            int skip,
            int take,
            string? sortField,
            bool sortDescending,
            IReadOnlyCollection<Guid>? visibleUserIds = null,
            CancellationToken cancellationToken = default)
        {
            VisibleUserIds = visibleUserIds;

            return Task.FromResult<(IReadOnlyList<User>, long)>(([], 0));
        }

        public Task<User?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<User?> FindUserByUsernameAsync(string username, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<User?> FindUserByEmailAsync(string email, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> EmailExistsAsync(
            string email, Guid? excludingUserId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void AddUser(User user) => throw new NotSupportedException();

        public Task<UserCredential?> FindCredentialAsync(
            Guid userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void AddCredential(UserCredential credential) => throw new NotSupportedException();

        public Task<IReadOnlyList<PasswordHistoryEntry>> GetPasswordHistoryAsync(
            Guid userId, int count, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void AddPasswordHistory(PasswordHistoryEntry entry) => throw new NotSupportedException();

        public Task PrunePasswordHistoryAsync(
            Guid userId, int keep, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PasswordResetToken?> FindPasswordResetTokenByHashAsync(
            string tokenHash, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<PasswordResetToken>> GetActivePasswordResetTokensAsync(
            Guid userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void AddPasswordResetToken(PasswordResetToken token) => throw new NotSupportedException();

        public Task<Session?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Session>> GetActiveSessionsAsync(
            Guid userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void AddSession(Session session) => throw new NotSupportedException();

        public Task<RefreshToken?> FindRefreshTokenByHashAsync(
            string tokenHash, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<RefreshToken>> GetTokenFamilyAsync(
            Guid familyId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<RefreshToken>> GetSessionRefreshTokensAsync(
            Guid sessionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void AddRefreshToken(RefreshToken token) => throw new NotSupportedException();

        public void AddLoginAttempt(LoginAttempt attempt) => throw new NotSupportedException();

        public Task<IReadOnlyList<LoginAttempt>> GetRecentLoginAttemptsAsync(
            Guid userId, int count, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
