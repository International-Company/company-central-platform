using CCP.Kernel.Paging;
using CCP.Kernel.Results;
using CCP.Modules.Identity.Application.Abstractions;
using CCP.Modules.Identity.Contracts.Dtos;
using CCP.Modules.Identity.Domain.Sessions;
using CCP.Modules.Identity.Domain.Users;

namespace CCP.Modules.Identity.Application.Users;

/// <summary>A filtered, sorted page of users.</summary>
/// <param name="UnitPathPrefixes">
/// The units the caller's permission reaches, as materialized-path prefixes.
/// </param>
/// <param name="ScopeRestricted">
/// Whether the caller's permission stops short of the whole company.
/// <para>
/// Carried separately from the prefixes because the two say different things. No
/// prefixes and unrestricted means "everybody"; no prefixes and restricted means
/// <b>nobody</b>, and conflating them turns a scope that grants nothing into a
/// scope that grants the lot.
/// </para>
/// </param>
public sealed record SearchUsersQuery(
    PageRequest Page,
    string? SearchTerm,
    string? Status,
    IReadOnlyList<string> UnitPathPrefixes,
    bool ScopeRestricted);

/// <summary>
/// Lists users for administration.
/// <para>
/// Sortable fields are allow-listed here rather than passed through, because an
/// arbitrary sort column means a scan on an unindexed column — an easy denial of
/// service on a large table (ADR-008 §11.4).
/// </para>
/// </summary>
public sealed class SearchUsersHandler(
    IIdentityRepository repository, IUserPlacement placement)
{
    /// <summary>Fields that are indexed and safe to sort by.</summary>
    public static readonly IReadOnlySet<string> SortableFields =
        new HashSet<string>(StringComparer.Ordinal) { "username", "displayName", "lastLoginAt", "createdAt" };

    public async Task<Result<PagedResult<UserDto>>> HandleAsync(
        SearchUsersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result<SortSpec?> sort = SortSpec.Parse(query.Page.Sort, SortableFields);

        if (sort.IsFailure)
        {
            return Result.Failure<PagedResult<UserDto>>(sort.Errors);
        }

        UserStatus? status = null;

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse(query.Status, ignoreCase: true, out UserStatus parsed))
            {
                return Result.Failure<PagedResult<UserDto>>(Error.Validation(
                    "IDENTITY.INVALID_STATUS_FILTER",
                    $"Unknown status. Allowed: {string.Join(", ", Enum.GetNames<UserStatus>())}.",
                    "status"));
            }

            status = parsed;
        }

        // What a scope means here, decided rather than left open.
        //
        // A user account has no department; the person behind it does, through
        // an employee record. So a department-scoped caller sees the accounts of
        // the people in their department and nothing else -- including no
        // account that has never been linked to an employee, because an account
        // with no place in the organization is in no department. The alternative
        // would show every service account to every unit administrator in the
        // company.
        //
        // The cost of that reading is real and worth stating: a unit-scoped
        // administrator who creates an account cannot see it until it is linked
        // to an employee, which is the act that puts it in the organization.
        IReadOnlyList<Guid>? visible = query.ScopeRestricted
            ? await placement.GetUserIdsUnderAsync(query.UnitPathPrefixes, cancellationToken)
            : null;

        (IReadOnlyList<User> items, long total) = await repository.SearchUsersAsync(
            query.SearchTerm,
            status,
            query.Page.Skip,
            query.Page.PageSize,
            sort.Value?.Field,
            sort.Value?.Descending ?? false,
            visible,
            cancellationToken);

        return Result.Success(new PagedResult<UserDto>(
            [.. items.Select(UserMapper.ToDto)],
            query.Page.Page,
            query.Page.PageSize,
            total));
    }
}

/// <summary>One user by id.</summary>
public sealed record GetUserQuery(Guid UserId);

public sealed class GetUserHandler(IIdentityRepository repository)
{
    public async Task<Result<UserDto>> HandleAsync(
        GetUserQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        User? user = await repository.FindUserByIdAsync(query.UserId, cancellationToken);

        return user is null
            ? Result.Failure<UserDto>(IdentityErrors.UserNotFound)
            : Result.Success(UserMapper.ToDto(user));
    }
}

/// <summary>The caller's own sessions.</summary>
public sealed record GetMySessionsQuery(Guid UserId, Guid CurrentSessionId);

/// <summary>
/// Lists the caller's active sessions so they can see where they are signed in
/// and end anything they do not recognise.
/// <para>
/// Carries no token or token hash — only enough to recognise a session. A "your
/// sessions" screen that exposed credentials would defeat its own purpose.
/// </para>
/// </summary>
public sealed class GetMySessionsHandler(IIdentityRepository repository)
{
    public async Task<Result<IReadOnlyList<SessionDto>>> HandleAsync(
        GetMySessionsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<Session> sessions =
            await repository.GetActiveSessionsAsync(query.UserId, cancellationToken);

        return Result.Success<IReadOnlyList<SessionDto>>(
        [
            .. sessions.Select(s => new SessionDto(
                s.Id,
                s.IpAddress,
                s.UserAgent,
                s.CreatedAt,
                s.LastActivityAt,
                s.AbsoluteExpiresAt,
                IsCurrent: s.Id == query.CurrentSessionId))
        ]);
    }
}

/// <summary>The caller's own recent sign-in history.</summary>
public sealed record GetMyLoginHistoryQuery(Guid UserId, int Count);

/// <summary>
/// Returns the caller's recent sign-in attempts, successful and failed.
/// <para>
/// Failures are included deliberately: a user seeing failed attempts they did
/// not make is the earliest signal that someone is trying their account, and it
/// costs nothing to show them.
/// </para>
/// </summary>
public sealed class GetMyLoginHistoryHandler(IIdentityRepository repository)
{
    private const int MaxCount = 50;

    public async Task<Result<IReadOnlyList<LoginAttemptDto>>> HandleAsync(
        GetMyLoginHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int count = Math.Clamp(query.Count, 1, MaxCount);

        IReadOnlyList<LoginAttempt> attempts =
            await repository.GetRecentLoginAttemptsAsync(query.UserId, count, cancellationToken);

        return Result.Success<IReadOnlyList<LoginAttemptDto>>(
        [
            // The internal failure reason is not projected. It is for operators,
            // and telling a caller which of several checks failed would leak
            // more than it helps.
            .. attempts.Select(a => new LoginAttemptDto(
                a.Succeeded, a.IpAddress, a.UserAgent, a.OccurredAt))
        ]);
    }
}
