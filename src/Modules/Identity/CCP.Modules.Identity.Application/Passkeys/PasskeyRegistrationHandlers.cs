using System.Buffers.Text;
using System.Security.Cryptography;
using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Identity.Application.Abstractions;
using CCP.Modules.Identity.Contracts.Dtos;
using CCP.Modules.Identity.Domain.Credentials;
using CCP.Modules.Identity.Domain.Users;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Identity.Application.Passkeys;

/// <summary>Asks to add a passkey, proving it is really this person first.</summary>
public sealed record BeginPasskeyRegistrationCommand(Guid UserId, string CurrentPassword);

/// <summary>The device's answer, plus what the person wants to call it.</summary>
public sealed record CompletePasskeyRegistrationCommand(
    Guid UserId,
    string Name,
    PasskeyRegistrationEvidence Evidence);

/// <summary>
/// Adding a passkey to an account.
/// <para>
/// <b>The current password is required, and that is not ceremony.</b> A passkey
/// is a new way into the account that survives a password change, so adding one
/// is precisely what somebody who had stolen a session would do to keep their
/// way in. Asking for the password means a stolen session alone is not enough:
/// it is the same reasoning that puts a password behind a password change.
/// </para>
/// <para>
/// A second factor is <b>not</b> demanded on top of it, deliberately. Step-up
/// here would mean only people who already have an authenticator app could ever
/// set up a fingerprint — which is the wrong way round, since the passkey is
/// the stronger credential of the two.
/// </para>
/// </summary>
public sealed class BeginPasskeyRegistrationHandler(
    IIdentityRepository repository,
    IPasswordHasher passwordHasher,
    IIdentityUnitOfWork unitOfWork,
    IClock clock,
    IOptions<IdentityOptions> options)
{
    private readonly IdentityOptions _options = options.Value;

    public async Task<Result<PasskeyRegistrationOptionsDto>> HandleAsync(
        BeginPasskeyRegistrationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        User? user = await repository.FindUserByIdAsync(command.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<PasskeyRegistrationOptionsDto>(IdentityErrors.UserNotFound);
        }

        UserCredential? credential = await repository.FindCredentialAsync(user.Id, cancellationToken);

        if (credential is null
            || !passwordHasher.Verify(command.CurrentPassword, credential.PasswordHash))
        {
            return Result.Failure<PasskeyRegistrationOptionsDto>(IdentityErrors.CurrentPasswordIncorrect);
        }

        DateTimeOffset now = clock.UtcNow;

        string challenge = NewChallenge();

        repository.AddWebAuthnChallenge(WebAuthnChallenge.Issue(
            challenge,
            WebAuthnCeremony.Registration,
            user.Id,
            now,
            _options.WebAuthn.ChallengeLifetime));

        // Swept here rather than by a background job: the rows are worthless
        // the moment they lapse, there is one per attempt, and the cheapest
        // place to delete them is a request that is already writing one.
        await repository.DeleteExpiredWebAuthnChallengesAsync(now, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        IReadOnlyList<WebAuthnCredential> existing =
            await repository.GetWebAuthnCredentialsAsync(user.Id, cancellationToken);

        return Result.Success(new PasskeyRegistrationOptionsDto(
            Challenge: challenge,
            RelyingPartyId: _options.WebAuthn.RelyingPartyId,
            RelyingPartyName: _options.WebAuthn.RelyingPartyName,

            // The account identifier the authenticator stores alongside the
            // key, and hands back at sign-in. The user's own id, so a passkey
            // keeps working through a change of username or email.
            UserId: Base64Url.EncodeToString(user.Id.ToByteArray(bigEndian: true)),
            Username: user.Username,
            DisplayName: user.DisplayName,

            // ES256 first: it is what a phone or a laptop produces, and the
            // shorter signature of the two. RS256 for the security keys that
            // only do RSA.
            Algorithms: [-7, -257],
            ExcludeCredentials: [.. existing.Select(c => c.CredentialId)],
            TimeoutMilliseconds: (int)_options.WebAuthn.ChallengeLifetime.TotalMilliseconds));
    }

    internal static string NewChallenge()
        => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
}

/// <summary>
/// Storing the passkey the device just made.
/// <para>
/// The challenge is spent before the response is checked, so a captured
/// exchange cannot be tried twice — not even once more by the person who sent
/// it.
/// </para>
/// </summary>
public sealed class CompletePasskeyRegistrationHandler(
    IIdentityRepository repository,
    IWebAuthnVerifier verifier,
    IIdentityUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<PasskeyDto>> HandleAsync(
        CompletePasskeyRegistrationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        string? presented = ReadChallenge(command.Evidence.ClientDataJson);

        if (presented is null)
        {
            return Result.Failure<PasskeyDto>(IdentityErrors.InvalidPasskeyRegistration);
        }

        WebAuthnChallenge? challenge =
            await repository.FindWebAuthnChallengeAsync(presented, cancellationToken);

        // Issued by this Platform, for this ceremony, for this person, and not
        // yet spent. Any of those failing makes the response meaningless
        // whatever its signature says.
        if (challenge is null
            || !challenge.IsUsableAt(now, WebAuthnCeremony.Registration)
            || challenge.UserId != command.UserId)
        {
            return Result.Failure<PasskeyDto>(IdentityErrors.InvalidPasskeyRegistration);
        }

        challenge.Redeem(now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        Result<VerifiedRegistration> verified =
            verifier.VerifyRegistration(command.Evidence, challenge.Value);

        if (verified.IsFailure)
        {
            return Result.Failure<PasskeyDto>(verified.Error);
        }

        // The same authenticator cannot hold two credentials with one
        // identifier, so this means the device is already registered — here or
        // on another account.
        WebAuthnCredential? clash =
            await repository.FindWebAuthnCredentialAsync(verified.Value.CredentialId, cancellationToken);

        if (clash is not null)
        {
            return Result.Failure<PasskeyDto>(IdentityErrors.PasskeyAlreadyRegistered);
        }

        var credential = WebAuthnCredential.Register(
            command.UserId,
            verified.Value.CredentialId,
            verified.Value.PublicKey,
            verified.Value.Algorithm,
            verified.Value.SignCount,
            verified.Value.AuthenticatorGuid,
            verified.Value.UserVerified,
            command.Name,
            now);

        repository.AddWebAuthnCredential(credential);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new PasskeyDto(
            credential.Id, credential.Name, credential.CreatedAt, credential.LastUsedAt));
    }

    /// <summary>
    /// The challenge the browser says it answered, read out so the real one can
    /// be looked up.
    /// <para>
    /// Read, never trusted. It is only a key into a table of challenges this
    /// Platform issued; the verifier then compares the row's value against the
    /// signed response. Using this value as the expected challenge would be the
    /// attacker choosing the question.
    /// </para>
    /// </summary>
    internal static string? ReadChallenge(string clientDataJson)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(
                Base64Url.DecodeFromChars(clientDataJson));

            return document.RootElement.TryGetProperty("challenge", out var challenge)
                ? challenge.GetString()
                : null;
        }
        catch (Exception exception)
            when (exception is FormatException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}

/// <summary>Somebody's own passkeys, and removing one.</summary>
public sealed class PasskeyQueryHandlers(
    IIdentityRepository repository,
    IIdentityUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<IReadOnlyList<PasskeyDto>>> ListAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<WebAuthnCredential> credentials =
            await repository.GetWebAuthnCredentialsAsync(userId, cancellationToken);

        return Result.Success<IReadOnlyList<PasskeyDto>>(
            [.. credentials.Select(c => new PasskeyDto(c.Id, c.Name, c.CreatedAt, c.LastUsedAt))]);
    }

    public async Task<Result> RemoveAsync(
        Guid userId, Guid passkeyId, CancellationToken cancellationToken = default)
    {
        WebAuthnCredential? credential =
            await repository.FindWebAuthnCredentialByIdAsync(passkeyId, cancellationToken);

        // Somebody else's passkey is reported as not found rather than as
        // forbidden: "that exists but is not yours" is an answer, and an
        // identifier somebody can probe is an identifier they can enumerate.
        if (credential is null || credential.UserId != userId || !credential.IsActive)
        {
            return Result.Failure(IdentityErrors.PasskeyNotFound);
        }

        credential.Revoke(clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
