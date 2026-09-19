using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Identity.Application.Abstractions;
using CCP.Modules.Identity.Contracts.Dtos;
using CCP.Modules.Identity.Contracts.Events;
using CCP.Modules.Identity.Domain.Credentials;
using CCP.Modules.Identity.Domain.Sessions;
using CCP.Modules.Identity.Domain.Users;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Identity.Application.Passkeys;

/// <summary>Asking for a challenge to sign in with. Carries no username.</summary>
public sealed record BeginPasskeySignInCommand(string? IpAddress);

/// <summary>The assertion, plus the request context a session is started from.</summary>
public sealed record CompletePasskeySignInCommand(
    PasskeyAssertionEvidence Evidence,
    string? IpAddress,
    string? UserAgent,
    string? DeviceFingerprint);

/// <summary>
/// Issues the challenge a passkey signs to prove it is here.
/// <para>
/// <b>It asks for nothing.</b> No username, no email, no hint of which account
/// is being reached for. The browser searches the device for a passkey
/// belonging to this Platform, and the Platform learns whose it is only from
/// the answer. Asking first would tell an anonymous caller which usernames
/// exist and which have passkeys — the enumeration this Platform refuses
/// everywhere else, and there is no reason to open it here for convenience
/// nobody asked for.
/// </para>
/// </summary>
public sealed class BeginPasskeySignInHandler(
    IIdentityRepository repository,
    IIdentityUnitOfWork unitOfWork,
    IClock clock,
    IOptions<IdentityOptions> options)
{
    private readonly IdentityOptions _options = options.Value;

    public async Task<Result<PasskeySignInOptionsDto>> HandleAsync(
        BeginPasskeySignInCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        string challenge = BeginPasskeyRegistrationHandler.NewChallenge();

        repository.AddWebAuthnChallenge(WebAuthnChallenge.Issue(
            challenge,
            WebAuthnCeremony.Authentication,

            // Nobody yet. Whose it is comes back with the signature.
            userId: null,
            now,
            _options.WebAuthn.ChallengeLifetime));

        await repository.DeleteExpiredWebAuthnChallengesAsync(now, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new PasskeySignInOptionsDto(
            Challenge: challenge,
            RelyingPartyId: _options.WebAuthn.RelyingPartyId,
            TimeoutMilliseconds: (int)_options.WebAuthn.ChallengeLifetime.TotalMilliseconds));
    }
}

/// <summary>
/// Signing in with a passkey.
/// <para>
/// The same session, the same tokens and the same history as a password
/// sign-in, reached by proving possession of a device that verified a human
/// rather than by repeating a shared secret.
/// </para>
/// <para>
/// <b>A failure here does not count towards lockout</b>, and that is a
/// deliberate difference from the password path. Lockout exists to stop
/// guessing, and a passkey cannot be guessed: there is nothing to try. What an
/// attacker could do, if failures counted, is send rubbish assertions naming a
/// credential they had seen once and lock a person out of their own account
/// from across the internet. Every attempt is still written to login history,
/// which is where a pattern of them would show.
/// </para>
/// </summary>
public sealed class CompletePasskeySignInHandler(
    IIdentityRepository repository,
    IWebAuthnVerifier verifier,
    ITokenService tokenService,
    IRefreshTokenGenerator refreshTokenGenerator,
    IIdentityOutbox outbox,
    IIdentityUnitOfWork unitOfWork,
    IClock clock,
    IOptions<IdentityOptions> options)
{
    private readonly IdentityOptions _options = options.Value;

    public async Task<Result<AuthenticationResultDto>> HandleAsync(
        CompletePasskeySignInCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        string? presented =
            CompletePasskeyRegistrationHandler.ReadChallenge(command.Evidence.ClientDataJson);

        if (presented is null)
        {
            return await RefuseAsync(null, null, "passkey_malformed", command, now, cancellationToken);
        }

        WebAuthnChallenge? challenge =
            await repository.FindWebAuthnChallengeAsync(presented, cancellationToken);

        if (challenge is null || !challenge.IsUsableAt(now, WebAuthnCeremony.Authentication))
        {
            return await RefuseAsync(null, null, "passkey_challenge_invalid", command, now, cancellationToken);
        }

        // Spent before anything is verified. A challenge left unspent because
        // the signature failed is one an attacker can keep trying against.
        challenge.Redeem(now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        WebAuthnCredential? credential = await repository.FindWebAuthnCredentialAsync(
            command.Evidence.CredentialId, cancellationToken);

        if (credential is null)
        {
            return await RefuseAsync(null, null, "passkey_unknown", command, now, cancellationToken);
        }

        User? user = await repository.FindUserByIdAsync(credential.UserId, cancellationToken);

        if (user is null)
        {
            return await RefuseAsync(null, null, "passkey_orphaned", command, now, cancellationToken);
        }

        Result<VerifiedAssertion> verified = verifier.VerifyAssertion(
            command.Evidence, challenge.Value, credential.PublicKey, credential.Algorithm);

        if (verified.IsFailure)
        {
            Result<AuthenticationResultDto> refusal = await RefuseAsync(
                user, verified.Error, "passkey_verification_failed", command, now, cancellationToken);

            return refusal;
        }

        // The device's claim about whose account this is. Checked against the
        // credential's own owner: they cannot disagree unless something is
        // wrong, and "cannot happen" is worth asserting where the answer
        // decides who somebody is signed in as.
        if (verified.Value.UserHandle is { } handle && handle != user.Id)
        {
            return await RefuseAsync(user, null, "passkey_user_handle_mismatch", command, now, cancellationToken);
        }

        Result counter = credential.RecordUse(verified.Value.SignCount, now);

        if (counter.IsFailure)
        {
            return await RefuseAsync(user, counter.Error, "passkey_counter", command, now, cancellationToken);
        }

        // Only now does the account's own state matter. Unlike the password
        // path there is no timing to protect here — the caller already proved
        // they hold the device — but the refusal stays uniform so a stolen
        // laptop learns nothing about the account it belongs to.
        Result canAuthenticate = user.CanAuthenticate(now);

        if (canAuthenticate.IsFailure)
        {
            return await RefuseAsync(
                user, null, ReasonFor(canAuthenticate.Error.Code), command, now, cancellationToken);
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

        repository.AddRefreshToken(RefreshToken.Issue(
            session.Id, user.Id, refreshTokenHash, now, _options.RefreshTokenLifetime));

        repository.AddLoginAttempt(LoginAttempt.Success(
            user.Id, user.Username, command.IpAddress, command.UserAgent, now));

        await outbox.EnqueueAsync(
            new LoginSucceededEvent(user.Id, user.Username, session.Id, command.IpAddress, now),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new AuthenticationResultDto(
            AccessToken: tokenService.CreateAccessToken(
                user.Id, user.Username, session.Id, now, user.MustChangePassword),
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
    /// Records the attempt and answers with one refusal for every cause.
    /// <para>
    /// The real reason goes to login history and to the event stream, where the
    /// people who need it can see it. The caller is told only that the passkey
    /// could not be used — with one exception, which is the device not having
    /// verified the person: that is not about the account, it is about the
    /// device, and it is the only refusal the person can act on.
    /// </para>
    /// </summary>
    private async Task<Result<AuthenticationResultDto>> RefuseAsync(
        User? user,
        Error? error,
        string reason,
        CompletePasskeySignInCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        repository.AddLoginAttempt(LoginAttempt.Failure(
            user?.Id,
            user?.Username ?? "(passkey)",
            reason,
            command.IpAddress,
            command.UserAgent,
            now));

        await outbox.EnqueueAsync(
            new LoginFailedEvent(user?.Id, user?.Username ?? "(passkey)", reason, command.IpAddress, now),
            cancellationToken);

        // Committed, as the password path commits its failures: the record of
        // an attempt must survive the refusal that produced it.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Failure<AuthenticationResultDto>(
            error?.Code == IdentityErrors.PasskeyUserVerificationRequired.Code
                ? IdentityErrors.PasskeyUserVerificationRequired
                : IdentityErrors.InvalidPasskeyAssertion);
    }

    private static string ReasonFor(string errorCode) => errorCode switch
    {
        "IDENTITY.ACCOUNT_DISABLED" => LoginFailureReasons.AccountDisabled,
        "IDENTITY.ACCOUNT_LOCKED" => LoginFailureReasons.AccountLocked,
        "IDENTITY.ACCOUNT_NOT_ACTIVATED" => LoginFailureReasons.AccountNotActivated,
        _ => "passkey_refused",
    };
}
