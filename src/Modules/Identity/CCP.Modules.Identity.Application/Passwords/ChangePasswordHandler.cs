using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Identity.Application.Abstractions;
using CCP.Modules.Identity.Domain.Credentials;
using CCP.Modules.Identity.Domain.Users;
using CCP.Modules.Identity.Domain.Users.Events;

namespace CCP.Modules.Identity.Application.Passwords;

/// <summary>A user changing their own password.</summary>
public sealed record ChangePasswordCommand(
    Guid UserId,
    Guid CurrentSessionId,
    string CurrentPassword,
    string NewPassword);

/// <summary>
/// Changes the caller's own password.
/// <para>
/// The current password is required even though the caller is already
/// authenticated. That is the point: an access token in the wrong hands should
/// not be enough to take an account permanently, and re-proving the password
/// converts a stolen session into a temporary problem rather than a permanent
/// one.
/// </para>
/// </summary>
public sealed class ChangePasswordHandler(
    IIdentityRepository repository,
    IPasswordHasher passwordHasher,
    PasswordSetter passwordSetter,
    IIdentityOutbox outbox,
    IIdentityUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        ChangePasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        User? user = await repository.FindUserByIdAsync(command.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure(IdentityErrors.UserNotFound);
        }

        UserCredential? credential = await repository.FindCredentialAsync(user.Id, cancellationToken);

        if (credential is null || !passwordHasher.Verify(command.CurrentPassword, credential.PasswordHash))
        {
            // No lockout increment here. This endpoint already requires a valid
            // token, so it is not a channel for guessing an unknown account's
            // password — and incrementing would let a stolen token lock the real
            // user out.
            return Result.Failure(IdentityErrors.CurrentPasswordIncorrect);
        }

        Result result = await passwordSetter.SetPasswordAsync(
            user,
            command.NewPassword,
            revokeOtherSessions: true,
            currentSessionId: command.CurrentSessionId,
            cancellationToken: cancellationToken);

        if (result.IsFailure)
        {
            return result;
        }

        await outbox.EnqueueAsync(
            new UserPasswordChangedEvent(user.Id, user.Username, now), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
