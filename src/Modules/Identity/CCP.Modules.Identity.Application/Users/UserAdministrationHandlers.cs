using CCP.Kernel.Application.Auditing;
using CCP.Kernel.Application.Security;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Identity.Application.Abstractions;
using CCP.Modules.Identity.Application.Passwords;
using CCP.Modules.Identity.Contracts.Dtos;
using CCP.Modules.Identity.Domain.Sessions;
using CCP.Modules.Identity.Domain.Users;
using CCP.Modules.Identity.Contracts.Events;

namespace CCP.Modules.Identity.Application.Users;

/// <summary>An administrator creating an account.</summary>
public sealed record CreateUserCommand(
    string Username,
    string Email,
    string DisplayName,
    string InitialPassword);

/// <summary>
/// Creates an account.
/// <para>
/// The new account always carries <c>MustChangePassword</c>, so the password an
/// administrator chose is a one-time handover credential rather than a lasting
/// one. Anyone who saw it — in a ticket, a chat message, over someone's
/// shoulder — loses access the moment the user signs in.
/// </para>
/// </summary>
public sealed class CreateUserHandler(
    IIdentityRepository repository,
    PasswordSetter passwordSetter,
    IIdentityOutbox outbox,
    IIdentityUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IClock clock)
{
    public async Task<Result<UserDto>> HandleAsync(
        CreateUserCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        Result<User> creation = User.Create(
            command.Username, command.Email, command.DisplayName, now, mustChangePassword: true);

        if (creation.IsFailure)
        {
            return Result.Failure<UserDto>(creation.Errors);
        }

        User user = creation.Value;

        // Checked here for a clear error message; the unique indexes are the
        // actual guarantee, since two concurrent requests could both pass this.
        if (await repository.UsernameExistsAsync(user.Username, cancellationToken))
        {
            return Result.Failure<UserDto>(IdentityErrors.UsernameTaken);
        }

        if (await repository.EmailExistsAsync(user.Email, null, cancellationToken))
        {
            return Result.Failure<UserDto>(IdentityErrors.EmailTaken);
        }

        repository.AddUser(user);

        // The initial password goes through the same policy, breach screening
        // and history as any other. An administrator cannot set a weak one.
        Result passwordResult = await passwordSetter.SetPasswordAsync(
            user, command.InitialPassword, revokeOtherSessions: false,
            cancellationToken: cancellationToken);

        if (passwordResult.IsFailure)
        {
            return Result.Failure<UserDto>(passwordResult.Errors);
        }

        // SetPassword clears the flag, because normally a password change means
        // the user chose it. Here they did not, so it is reinstated.
        user.RequirePasswordChange();

        await outbox.EnqueueAsync(
            new UserCreatedEvent(user.Id, user.Username, user.Email, now), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // A new account is a new way in. Recorded after the commit, so the
        // trail never claims something that then rolled back. The initial
        // password is not carried - it never reaches the trail at all.
        await auditTrail.RecordAsync(
            new AuditEntry(
                "identity",
                "user.created",
                AuditOutcome.Success,
                "user",
                user.Id.ToString(),
                NewValue: $$"""{"username":"{{user.Username}}","email":"{{user.Email}}","displayName":"{{user.DisplayName}}"}"""),
            cancellationToken);

        return Result.Success(UserMapper.ToDto(user));
    }
}

/// <summary>Editing an account's profile fields.</summary>
public sealed record UpdateUserCommand(Guid UserId, string Email, string DisplayName);

/// <summary>Updates the mutable profile fields of an account.</summary>
public sealed class UpdateUserHandler(
    IIdentityRepository repository,
    IIdentityOutbox outbox,
    IIdentityUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IClock clock)
{
    public async Task<Result<UserDto>> HandleAsync(
        UpdateUserCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        User? user = await repository.FindUserByIdAsync(command.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<UserDto>(IdentityErrors.UserNotFound);
        }

        if (await repository.EmailExistsAsync(command.Email, user.Id, cancellationToken))
        {
            return Result.Failure<UserDto>(IdentityErrors.EmailTaken);
        }

        Result result = user.ChangeEmail(command.Email, now)
            .Combine(user.ChangeDisplayName(command.DisplayName, now));

        if (result.IsFailure)
        {
            return Result.Failure<UserDto>(result.Errors);
        }

        // The username is deliberately not editable. It appears in audit records
        // across every company system, and letting it change would make an
        // existing trail ambiguous. Changing one is a create-and-disable.
        await outbox.EnqueueAsync(new UserUpdatedEvent(user.Id, user.Username, now), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                "identity",
                "user.updated",
                AuditOutcome.Success,
                "user",
                user.Id.ToString(),
                NewValue: $$"""{"email":"{{user.Email}}","displayName":"{{user.DisplayName}}"}"""),
            cancellationToken);

        return Result.Success(UserMapper.ToDto(user));
    }
}

/// <summary>Enabling, disabling or unlocking an account.</summary>
public sealed record ChangeUserStatusCommand(Guid UserId, Guid ActingUserId, UserStatusAction Action);

/// <summary>The administrative actions available on an account's state.</summary>
public enum UserStatusAction
{
    Enable = 1,
    Disable = 2,
    Unlock = 3
}

/// <summary>
/// Changes an account's state.
/// <para>
/// Disabling ends every session immediately. An account that is disabled but
/// whose sessions keep working is not disabled in any sense that matters — the
/// access token would keep being accepted until it expired, and the refresh
/// token for far longer.
/// </para>
/// </summary>
public sealed class ChangeUserStatusHandler(
    IIdentityRepository repository,
    IIdentityOutbox outbox,
    IIdentityUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IAdministratorSafety administrators,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        ChangeUserStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        // No administrator may disable or unlock their own account. Self-service
        // state changes are how someone locks the last administrator out, and
        // how a compromised session hides its tracks.
        if (command.UserId == command.ActingUserId)
        {
            return Result.Failure(IdentityErrors.CannotModifySelf);
        }

        User? user = await repository.FindUserByIdAsync(command.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure(IdentityErrors.UserNotFound);
        }

        switch (command.Action)
        {
            case UserStatusAction.Disable:
                {
                    // The last account that can grant a role to anybody.
                    //
                    // Identity does not know what a role is, and must not. It
                    // asks the kernel, which Authorization answers -- the same
                    // seam shape as the audit trail. Without this, disabling one
                    // account can leave a Platform that nobody can ever be given
                    // access to again, recoverable only by writing a row into
                    // the production database by hand.
                    if (await administrators.IsTheLastGrantingUserAsync(
                            user.Id, cancellationToken))
                    {
                        await auditTrail.RecordAsync(
                            new AuditEntry(
                                "identity",
                                "user.disabled",
                                AuditOutcome.Denied,
                                "user",
                                user.Id.ToString(),
                                Metadata: """{"reason":"last granting account"}"""),
                            cancellationToken);

                        return Result.Failure(IdentityErrors.WouldStrandThePlatform);
                    }

                    Result result = user.Disable(now);

                    if (result.IsFailure)
                    {
                        return result;
                    }

                    await RevokeAllSessionsAsync(
                        user.Id, SessionRevocationReasons.AccountDisabled, now, cancellationToken);

                    await outbox.EnqueueAsync(
                        new UserDisabledEvent(user.Id, user.Username, now), cancellationToken);

                    break;
                }

            case UserStatusAction.Enable:
                {
                    Result result = user.Enable(now);

                    if (result.IsFailure)
                    {
                        return result;
                    }

                    await outbox.EnqueueAsync(
                        new UserEnabledEvent(user.Id, user.Username, now), cancellationToken);

                    break;
                }

            case UserStatusAction.Unlock:
                {
                    user.Unlock(now);

                    await outbox.EnqueueAsync(
                        new UserUnlockedEvent(user.Id, user.Username, now), cancellationToken);

                    break;
                }

            default:
                return Result.Failure(Error.Rule(
                    "IDENTITY.UNKNOWN_STATUS_ACTION", "The requested action is not supported."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Enabling restores a way in that something deliberately closed;
        // disabling is how an intruder removes the person who would notice;
        // unlocking undoes a lockout that was doing its job. All three belong
        // on the record.
        await auditTrail.RecordAsync(
            new AuditEntry(
                "identity",
                $"user.{command.Action.ToString().ToLowerInvariant()}",
                AuditOutcome.Success,
                "user",
                command.UserId.ToString()),
            cancellationToken);

        return Result.Success();
    }

    private async Task RevokeAllSessionsAsync(
        Guid userId,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Session> sessions =
            await repository.GetActiveSessionsAsync(userId, cancellationToken);

        foreach (Session session in sessions)
        {
            session.Revoke(reason, now);

            IReadOnlyList<RefreshToken> tokens =
                await repository.GetSessionRefreshTokensAsync(session.Id, cancellationToken);

            foreach (RefreshToken token in tokens)
            {
                token.Revoke(reason, now);
            }

            await outbox.EnqueueAsync(
                new SessionRevokedEvent(session.Id, userId, reason, now), cancellationToken);
        }
    }
}

/// <summary>
/// Maps entities to their public shape.
/// <para>
/// Hand-written, and that is the point: this is the boundary that decides what
/// leaves the module. An automatic mapper would add fields as the entity gains
/// them, silently — which is exactly how internal state ends up in a response.
/// </para>
/// </summary>
public static class UserMapper
{
    public static UserDto ToDto(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return new UserDto(
            user.Id,
            user.Username,
            user.Email,
            user.EmailVerified,
            user.DisplayName,
            user.Status.ToString(),
            user.MustChangePassword,
            user.LastLoginAt,
            user.CreatedAt,
            user.PreferredLocale);
    }
}
