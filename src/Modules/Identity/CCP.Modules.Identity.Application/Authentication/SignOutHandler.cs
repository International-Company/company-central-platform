using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Application.Events;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Identity.Application.Abstractions;
using CCP.Modules.Identity.Domain.Sessions;
using CCP.Modules.Identity.Domain.Users;
using CCP.Modules.Identity.Domain.Users.Events;

namespace CCP.Modules.Identity.Application.Authentication;

/// <summary>Ends one session.</summary>
public sealed record SignOutCommand(Guid SessionId, Guid UserId);

/// <summary>
/// Signs a user out by revoking the session and its refresh tokens
/// <b>server-side</b>.
/// <para>
/// Deleting a cookie is not signing out. A refresh token that was copied before
/// the cookie was cleared still works, so the only meaningful sign-out is one
/// that invalidates the credential at the server (ARCHITECTURE.md §13.5).
/// </para>
/// </summary>
public sealed class SignOutHandler(
    IIdentityRepository repository,
    IIdentityOutbox outbox,
    IIdentityUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        SignOutCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        Session? session = await repository.FindSessionAsync(command.SessionId, cancellationToken);

        if (session is null)
        {
            return Result.Failure(IdentityErrors.SessionNotFound);
        }

        // A user may only end their own session here. Ending someone else's is
        // an administrative action with its own endpoint and its own permission.
        if (session.UserId != command.UserId)
        {
            return Result.Failure(IdentityErrors.SessionNotFound);
        }

        // Already revoked is success, not an error: the caller wanted the
        // session ended, and it is ended.
        if (session.IsRevoked)
        {
            return Result.Success();
        }

        session.Revoke(SessionRevocationReasons.UserSignedOut, now);

        await RevokeSessionTokensAsync(session, SessionRevocationReasons.UserSignedOut, now, cancellationToken);

        await outbox.EnqueueAsync(
            new SessionRevokedEvent(session.Id, session.UserId, SessionRevocationReasons.UserSignedOut, now),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// Revokes every refresh token belonging to the session.
    /// <para>
    /// Found through the family of the session's current token: revoking only
    /// the token last presented would leave earlier, unspent siblings valid.
    /// </para>
    /// </summary>
    private async Task RevokeSessionTokensAsync(
        Session session,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RefreshToken> tokens =
            await repository.GetSessionRefreshTokensAsync(session.Id, cancellationToken);

        foreach (RefreshToken token in tokens)
        {
            token.Revoke(reason, now);
        }
    }
}
