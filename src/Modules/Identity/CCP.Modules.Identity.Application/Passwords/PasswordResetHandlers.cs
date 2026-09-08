using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Identity.Application.Abstractions;
using CCP.Modules.Identity.Domain.Credentials;
using CCP.Modules.Identity.Domain.Users;
using CCP.Modules.Identity.Domain.Users.Events;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Identity.Application.Passwords;

/// <summary>A request to begin a password reset. Identified by email.</summary>
public sealed record RequestPasswordResetCommand(string Email, string? IpAddress);

/// <summary>
/// Begins a password reset.
/// <para>
/// <b>Enumeration-safe by construction.</b> This handler always succeeds, from
/// the caller's point of view, whether or not the address belongs to an account.
/// The endpoint is anonymous, so any observable difference — a different status
/// code, a different message, or a measurably different response time — would
/// turn it into a free tool for discovering who works at the company.
/// </para>
/// <para>
/// That is why the work is deliberately similar in both cases, and why the
/// method returns nothing about what it found.
/// </para>
/// </summary>
public sealed class RequestPasswordResetHandler(
    IIdentityRepository repository,
    IRefreshTokenGenerator tokenGenerator,
    IIdentityOutbox outbox,
    IIdentityUnitOfWork unitOfWork,
    IClock clock,
    IOptions<IdentityOptions> options)
{
    private readonly IdentityOptions _options = options.Value;

    /// <summary>
    /// Always returns success. The token, when one is issued, is delivered by
    /// email — it is never returned to the caller, which is the whole basis of
    /// the flow's security.
    /// </summary>
    public async Task<Result> HandleAsync(
        RequestPasswordResetCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        User? user = await repository.FindUserByEmailAsync(command.Email, cancellationToken);

        // Unknown address, or an account that could not sign in anyway: do
        // nothing, and say nothing. Returning "no such user" here would leak the
        // company's entire address list to an anonymous caller.
        if (user is null || user.Status is UserStatus.Disabled or UserStatus.PendingActivation)
        {
            return Result.Success();
        }

        // A newly issued token supersedes any outstanding one, so a stale link
        // in an old email cannot be redeemed later.
        IReadOnlyList<PasswordResetToken> outstanding =
            await repository.GetActivePasswordResetTokensAsync(user.Id, cancellationToken);

        foreach (PasswordResetToken existing in outstanding)
        {
            existing.Invalidate(now);
        }

        (string tokenValue, string tokenHash) = tokenGenerator.Generate();

        repository.AddPasswordResetToken(PasswordResetToken.Issue(
            user.Id, tokenHash, now, _options.PasswordResetTokenLifetime, command.IpAddress));

        // The Notifications module (Phase 9) subscribes to this and sends the
        // email containing the token. Until then the event is recorded and
        // nothing is delivered — which is stated in the module documentation
        // rather than left to be discovered.
        await outbox.EnqueueAsync(
            new PasswordResetRequestedEvent(
                user.Id, user.Username, user.Email, tokenValue, now.Add(_options.PasswordResetTokenLifetime), now),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Redeems a reset token and sets a new password.</summary>
public sealed record ResetPasswordCommand(string Token, string NewPassword, string? IpAddress);

/// <summary>
/// Completes a password reset.
/// <para>
/// The token is looked up by hash, must be unspent, uninvalidated and unexpired,
/// and is consumed on success. A successful reset ends every other session,
/// because whoever prompted the reset may be holding one.
/// </para>
/// </summary>
public sealed class ResetPasswordHandler(
    IIdentityRepository repository,
    IRefreshTokenGenerator tokenGenerator,
    PasswordSetter passwordSetter,
    IIdentityOutbox outbox,
    IIdentityUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        ResetPasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        string tokenHash = tokenGenerator.HashToken(command.Token);

        PasswordResetToken? resetToken =
            await repository.FindPasswordResetTokenByHashAsync(tokenHash, cancellationToken);

        // One error for every failure mode — unknown, spent, superseded or
        // expired. Distinguishing them would tell a holder of an old link
        // whether it was ever valid.
        if (resetToken is null || !resetToken.IsUsable(now))
        {
            return Result.Failure(IdentityErrors.InvalidPasswordResetToken);
        }

        User? user = await repository.FindUserByIdAsync(resetToken.UserId, cancellationToken);

        if (user is null || user.Status is UserStatus.Disabled or UserStatus.PendingActivation)
        {
            return Result.Failure(IdentityErrors.InvalidPasswordResetToken);
        }

        // Every session ends: the reset was likely prompted by a compromise, and
        // leaving existing sessions alive would make the reset cosmetic.
        Result result = await passwordSetter.SetPasswordAsync(
            user,
            command.NewPassword,
            revokeOtherSessions: true,
            currentSessionId: null,
            cancellationToken: cancellationToken);

        if (result.IsFailure)
        {
            // The token survives a rejected password so the user can try a
            // different one, rather than having to request a whole new link
            // because their first choice was too short.
            return result;
        }

        resetToken.MarkUsed(now);

        // A lockout must not outlive the reset, or a user who locked themselves
        // out and then reset would still be unable to sign in.
        user.Unlock(now);

        await outbox.EnqueueAsync(
            new PasswordResetCompletedEvent(user.Id, user.Username, command.IpAddress, now),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
