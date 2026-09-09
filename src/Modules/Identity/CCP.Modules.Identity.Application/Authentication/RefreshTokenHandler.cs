using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Application.Events;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Identity.Application.Abstractions;
using CCP.Modules.Identity.Contracts.Dtos;
using CCP.Modules.Identity.Domain.Sessions;
using CCP.Modules.Identity.Domain.Users;
using CCP.Modules.Identity.Contracts.Events;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Identity.Application.Authentication;

/// <summary>A refresh request, with the context needed to record it.</summary>
public sealed record RefreshCommand(string RefreshToken, string? IpAddress, string? UserAgent);

/// <summary>
/// Exchanges a refresh token for a new access token and a new refresh token.
/// <para>
/// <b>Rotation with reuse detection</b> (ADR-006). Every refresh spends the
/// presented token and issues a fresh one. If a token that was already spent is
/// presented again, the session family is revoked entirely and a security event
/// is raised.
/// </para>
/// <para>
/// The reasoning: a refresh token is a bearer credential with a long life. If it
/// is copied, both the thief and the legitimate user hold a valid token, and
/// nothing in a non-rotating design distinguishes them — the compromise is
/// silent and lasts until expiry. With rotation, whichever party refreshes
/// second presents a spent token, and that single observation converts an
/// undetectable compromise into a detected incident. The legitimate user is
/// signed out and must authenticate again, which is a small price for closing
/// the theft.
/// </para>
/// </summary>
public sealed class RefreshTokenHandler(
    IIdentityRepository repository,
    ITokenService tokenService,
    IRefreshTokenGenerator refreshTokenGenerator,
    IIdentityOutbox outbox,
    IIdentityUnitOfWork unitOfWork,
    IClock clock,
    IOptions<IdentityOptions> options)
{
    private readonly IdentityOptions _options = options.Value;

    public async Task<Result<AuthenticationResultDto>> HandleAsync(
        RefreshCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        // Look up by hash: the plaintext token is never stored, so a leaked
        // database backup yields nothing usable.
        string presentedHash = refreshTokenGenerator.HashToken(command.RefreshToken);

        RefreshToken? token = await repository.FindRefreshTokenByHashAsync(presentedHash, cancellationToken);

        if (token is null)
        {
            return Result.Failure<AuthenticationResultDto>(IdentityErrors.InvalidRefreshToken);
        }

        // --- Reuse detection ------------------------------------------------
        // A spent token presented again means the credential was copied.
        if (token.IsUsed)
        {
            await HandleReuseAsync(token, command, now, cancellationToken);

            return Result.Failure<AuthenticationResultDto>(IdentityErrors.RefreshTokenReused);
        }

        if (!token.IsUsable(now))
        {
            return Result.Failure<AuthenticationResultDto>(IdentityErrors.InvalidRefreshToken);
        }

        Session? session = await repository.FindSessionAsync(token.SessionId, cancellationToken);

        if (session is null || !session.IsActive(now, _options.SessionIdleTimeout))
        {
            return Result.Failure<AuthenticationResultDto>(IdentityErrors.SessionExpired);
        }

        User? user = await repository.FindUserByIdAsync(token.UserId, cancellationToken);

        // Re-check the account on every refresh. An account disabled after
        // sign-in must not be able to extend its session indefinitely.
        if (user is null || user.CanAuthenticate(now).IsFailure)
        {
            session.Revoke(SessionRevocationReasons.AccountDisabled, now);
            token.Revoke(SessionRevocationReasons.AccountDisabled, now);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<AuthenticationResultDto>(IdentityErrors.InvalidRefreshToken);
        }

        // --- Rotate ---------------------------------------------------------
        (string newTokenValue, string newTokenHash) = refreshTokenGenerator.Generate();

        RefreshToken replacement = RefreshToken.IssueInFamily(
            session.Id, user.Id, token.FamilyId, newTokenHash, now, _options.RefreshTokenLifetime);

        repository.AddRefreshToken(replacement);

        token.MarkUsed(replacement.Id, now);
        session.Touch(now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        string accessToken = tokenService.CreateAccessToken(user.Id, user.Username, session.Id, now);

        return Result.Success(new AuthenticationResultDto(
            AccessToken: accessToken,
            RefreshToken: newTokenValue,
            ExpiresInSeconds: (int)tokenService.AccessTokenLifetime.TotalSeconds,
            TokenType: "Bearer",
            User: new CurrentUserDto(
                user.Id,
                user.Username,
                user.Email,
                user.DisplayName,
                user.MustChangePassword,
                session.Id)));
    }

    /// <summary>
    /// Revokes the entire token family and its session, and raises a security
    /// event.
    /// <para>
    /// The whole family, not just the replayed token: the thief may already have
    /// rotated several times, so revoking one token would leave them holding a
    /// valid successor. Revoking the family invalidates every descendant of that
    /// sign-in at once.
    /// </para>
    /// </summary>
    private async Task HandleReuseAsync(
        RefreshToken reusedToken,
        RefreshCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RefreshToken> family =
            await repository.GetTokenFamilyAsync(reusedToken.FamilyId, cancellationToken);

        foreach (RefreshToken familyMember in family)
        {
            familyMember.Revoke(SessionRevocationReasons.RefreshTokenReuseDetected, now);
        }

        Session? session = await repository.FindSessionAsync(reusedToken.SessionId, cancellationToken);
        session?.Revoke(SessionRevocationReasons.RefreshTokenReuseDetected, now);

        User? user = await repository.FindUserByIdAsync(reusedToken.UserId, cancellationToken);

        await outbox.EnqueueAsync(
            new RefreshTokenReusedEvent(
                reusedToken.UserId,
                user?.Username ?? "(unknown)",
                reusedToken.SessionId,
                command.IpAddress,
                now),
            cancellationToken);

        await outbox.EnqueueAsync(
            new SessionRevokedEvent(
                reusedToken.SessionId,
                reusedToken.UserId,
                SessionRevocationReasons.RefreshTokenReuseDetected,
                now),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
