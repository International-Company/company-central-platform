using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Application.Events;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Identity.Application.Abstractions;
using CCP.Modules.Identity.Contracts.Dtos;
using CCP.Modules.Identity.Domain.Credentials;
using CCP.Modules.Identity.Domain.Sessions;
using CCP.Modules.Identity.Domain.Users;
using CCP.Modules.Identity.Contracts.Events;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Identity.Application.Authentication;

/// <summary>Credentials presented at sign-in, plus the request context.</summary>
public sealed record SignInCommand(
    string Username,
    string Password,
    string? IpAddress,
    string? UserAgent,
    string? DeviceFingerprint);

/// <summary>
/// Authenticates a user and starts a session.
/// <para>
/// The security properties this handler must have, and which its tests assert:
/// </para>
/// <list type="number">
/// <item>
/// <b>Uniform failure.</b> An unknown username, a wrong password, a disabled
/// account and a locked account all return the same error. Any difference lets
/// an attacker enumerate accounts.
/// </item>
/// <item>
/// <b>Uniform timing.</b> When the username is unknown, a hash verification is
/// still performed against a dummy hash. Without it, an unknown username returns
/// in microseconds and a real one in ~100ms, and that difference alone
/// enumerates the directory.
/// </item>
/// <item>
/// <b>Failures are recorded even when they cost nothing.</b> An attempt against
/// a username that does not exist is still written to login history, because
/// that pattern is what credential stuffing looks like.
/// </item>
/// <item>
/// <b>The state check happens after password verification.</b> Checking whether
/// an account is disabled before verifying the password would reveal, by timing,
/// that the account exists.
/// </item>
/// </list>
/// </summary>
public sealed class SignInHandler(
    IIdentityRepository repository,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IRefreshTokenGenerator refreshTokenGenerator,
    IIdentityOutbox outbox,
    IIdentityUnitOfWork unitOfWork,
    IClock clock,
    IOptions<IdentityOptions> options)
{
    private readonly IdentityOptions _options = options.Value;

    /// <summary>
    /// A valid Argon2id hash of a value nobody knows, verified against when the
    /// username does not exist so that the work done is the same either way.
    /// Computed once at startup, because computing it per request would itself
    /// be a timing signal.
    /// </summary>
    private readonly Lazy<string> _dummyHash = new(() =>
        passwordHasher.Hash(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))));

    public async Task<Result<AuthenticationResultDto>> HandleAsync(
        SignInCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        User? user = await repository.FindUserByUsernameAsync(command.Username, cancellationToken);

        if (user is null)
        {
            // Do the same work as a real verification, so an unknown username
            // is indistinguishable by timing from a wrong password.
            passwordHasher.Verify(command.Password, _dummyHash.Value);

            await RecordFailureAsync(
                null, command, LoginFailureReasons.UnknownUsername, now, cancellationToken);

            return Result.Failure<AuthenticationResultDto>(IdentityErrors.InvalidCredentials);
        }

        UserCredential? credential = await repository.FindCredentialAsync(user.Id, cancellationToken);

        // An account with no credential cannot sign in, and must still consume
        // the same time as one that can.
        bool passwordValid = credential is not null
            && passwordHasher.Verify(command.Password, credential.PasswordHash);

        if (!passwordValid)
        {
            user.RecordFailedLogin(now, _options.Lockout);

            await RecordFailureAsync(
                user, command, LoginFailureReasons.WrongPassword, now, cancellationToken);

            return Result.Failure<AuthenticationResultDto>(IdentityErrors.InvalidCredentials);
        }

        // Only now, with the password proven, does account state matter. Doing
        // this earlier would leak the account's existence through timing.
        Result canAuthenticate = user.CanAuthenticate(now);

        if (canAuthenticate.IsFailure)
        {
            await RecordFailureAsync(
                user, command, ReasonFor(canAuthenticate.Error.Code), now, cancellationToken);

            // The real reason is recorded above; the caller gets the uniform one.
            return Result.Failure<AuthenticationResultDto>(IdentityErrors.InvalidCredentials);
        }

        // Opportunistically upgrade a hash produced with weaker parameters. This
        // is the only moment the plaintext is known, so it is the only moment
        // the upgrade is possible.
        if (passwordHasher.NeedsRehash(credential!.PasswordHash))
        {
            credential.Update(passwordHasher.Hash(command.Password), passwordHasher.AlgorithmId, now);
        }

        user.RecordSuccessfulLogin(now);

        Session session = Session.Start(
            user.Id,
            command.IpAddress,
            command.UserAgent,
            command.DeviceFingerprint,
            now,
            _options.SessionAbsoluteLifetime);

        repository.AddSession(session);

        (string refreshTokenValue, string refreshTokenHash) = refreshTokenGenerator.Generate();

        RefreshToken refreshToken = RefreshToken.Issue(
            session.Id, user.Id, refreshTokenHash, now, _options.RefreshTokenLifetime);

        repository.AddRefreshToken(refreshToken);

        repository.AddLoginAttempt(LoginAttempt.Success(
            user.Id, user.Username, command.IpAddress, command.UserAgent, now));

        await outbox.EnqueueAsync(
            new LoginSucceededEvent(user.Id, user.Username, session.Id, command.IpAddress, now),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        string accessToken = tokenService.CreateAccessToken(
            user.Id, user.Username, session.Id, now, user.MustChangePassword);

        return Result.Success(new AuthenticationResultDto(
            AccessToken: accessToken,
            RefreshToken: refreshTokenValue,
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
    /// Records a failed attempt in history and on the outbox, then commits.
    /// <para>
    /// The commit matters: failure state — the incremented attempt count and any
    /// resulting lockout — must persist. Returning early without saving would
    /// make lockout unenforceable, because every attempt would start from zero.
    /// </para>
    /// </summary>
    private async Task RecordFailureAsync(
        User? user,
        SignInCommand command,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        repository.AddLoginAttempt(LoginAttempt.Failure(
            user?.Id, command.Username, reason, command.IpAddress, command.UserAgent, now));

        await outbox.EnqueueAsync(
            new LoginFailedEvent(user?.Id, command.Username, reason, command.IpAddress, now),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static string ReasonFor(string errorCode) => errorCode switch
    {
        "IDENTITY.ACCOUNT_DISABLED" => LoginFailureReasons.AccountDisabled,
        "IDENTITY.ACCOUNT_LOCKED" => LoginFailureReasons.AccountLocked,
        "IDENTITY.ACCOUNT_NOT_ACTIVATED" => LoginFailureReasons.AccountNotActivated,
        _ => LoginFailureReasons.WrongPassword
    };
}
