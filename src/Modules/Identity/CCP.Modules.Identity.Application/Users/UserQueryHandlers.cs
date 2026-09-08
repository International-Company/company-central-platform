using CCP.Kernel.Paging;
using CCP.Kernel.Results;
using CCP.Modules.Identity.Application.Abstractions;
using CCP.Modules.Identity.Contracts.Dtos;
using CCP.Modules.Identity.Domain.Sessions;
using CCP.Modules.Identity.Domain.Users;

namespace CCP.Modules.Identity.Application.Users;

/// <summary>A filtered, sorted page of users.</summary>
public sealed record SearchUsersQuery(PageRequest Page, string? SearchTerm, string? Status);

/// <summary>
/// Lists users for administration.
/// <para>
/// Sortable fields are allow-listed here rather than passed through, because an
/// arbitrary sort column means a scan on an unindexed column — an easy denial of
/// service on a large table (ADR-008 §11.4).
/// </para>
/// </summary>
public sealed class SearchUsersHandler(IIdentityRepository repository)
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

        (IReadOnlyList<User> items, long total) = await repository.SearchUsersAsync(
            query.SearchTerm,
            status,
            query.Page.Skip,
            query.Page.PageSize,
            sort.Value?.Field,
            sort.Value?.Descending ?? false,
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
