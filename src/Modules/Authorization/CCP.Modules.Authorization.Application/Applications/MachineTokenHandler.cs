using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Authorization.Application.Abstractions;
using CCP.Modules.Authorization.Contracts.Dtos;
using CCP.Modules.Authorization.Domain;
using CCP.Modules.Authorization.Domain.Applications;

namespace CCP.Modules.Authorization.Application.Applications;

/// <summary>
/// A client-credentials token request.
/// </summary>
/// <param name="ClientId">The public half of the credential.</param>
/// <param name="ClientSecret">The half that proves it.</param>
/// <param name="OnBehalfOfUserId">
/// The person to act for, or null to act as the application itself.
/// </param>
public sealed record MachineTokenCommand(
    string ClientId, string ClientSecret, Guid? OnBehalfOfUserId);

/// <summary>
/// Exchanges a client credential for a short-lived access token.
/// <para>
/// <b>Every refusal returns the same error.</b> A wrong secret, a client id
/// nobody has ever held, a revoked credential, an expired one, a disabled
/// application — all of them answer <c>AUTHZ.INVALID_CLIENT</c>. Anything more
/// helpful is an oracle: an attacker with a list of guessed client ids would
/// learn which ones exist, and one that learned "revoked" would know it had
/// found a real key a moment too late.
/// </para>
/// <para>
/// The Platform's own log records which of those it actually was, because the
/// distinction matters enormously to the operator and not at all to the caller.
/// </para>
/// </summary>
public sealed class MachineTokenHandler(
    IApplicationRepository applications,
    IAuthorizationRepository repository,
    IApplicationSecretHasher hasher,
    IMachineTokenIssuer issuer,
    IDelegationSubjectVerifier delegationSubjects,
    IPermissionResolver resolver,
    IAuthorizationUnitOfWork unitOfWork,
    IClock clock)
{
    /// <summary>
    /// The permission an application needs before it may act as somebody.
    /// <para>
    /// A separate gate from the permissions the delegated call will then be
    /// checked against. An application that may read employees should not
    /// thereby be able to read them <i>as</i> the finance director, and the
    /// difference between those two is worth a deliberate grant.
    /// </para>
    /// </summary>
    public const string DelegationPermission = HandlerPermissions.ActOnBehalf;

    public async Task<Result<MachineTokenDto>> HandleAsync(
        MachineTokenCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        if (string.IsNullOrWhiteSpace(command.ClientId)
            || string.IsNullOrWhiteSpace(command.ClientSecret))
        {
            return Result.Failure<MachineTokenDto>(AuthorizationErrors.InvalidClient);
        }

        ApplicationCredential? credential =
            await applications.FindCredentialByClientIdAsync(command.ClientId, cancellationToken);

        // The comparison runs even when no credential was found, against a hash
        // that cannot match. Returning early here would make a valid client id
        // measurably slower to reject than an invented one, which is the same
        // oracle by a different route.
        bool secretMatches = hasher.Matches(
            command.ClientSecret,
            credential?.SecretHash ?? string.Empty);

        if (credential is null || !secretMatches || !credential.IsLive(now))
        {
            return Result.Failure<MachineTokenDto>(AuthorizationErrors.InvalidClient);
        }

        RegisteredApplication? application =
            await repository.FindApplicationAsync(credential.ApplicationId, cancellationToken);

        if (application is null || !application.IsActive)
        {
            return Result.Failure<MachineTokenDto>(AuthorizationErrors.InvalidClient);
        }

        if (command.OnBehalfOfUserId is { } subject)
        {
            Result delegation = await CheckDelegationAsync(application, subject, cancellationToken);

            if (delegation.IsFailure)
            {
                return Result.Failure<MachineTokenDto>(delegation.Errors);
            }
        }

        (string token, TimeSpan lifetime) = issuer.IssueMachineToken(
            application.Id, application.Code, credential.ClientId, command.OnBehalfOfUserId, now);

        // Stamped so that "can we revoke the old key yet?" is answered by
        // evidence rather than by hoping. Without it a rotation never finishes,
        // because nobody is willing to be the one who breaks production.
        credential.RecordUse(now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new MachineTokenDto(
            token, "Bearer", (int)lifetime.TotalSeconds));
    }

    /// <summary>
    /// Two questions before an application may act as somebody: is it allowed to
    /// at all, and can that person sign in?
    /// </summary>
    private async Task<Result> CheckDelegationAsync(
        RegisteredApplication application,
        Guid onBehalfOfUserId,
        CancellationToken cancellationToken)
    {
        Domain.Scopes.AccessDecision permitted = await resolver.EvaluateForApplicationAsync(
            application.Id, DelegationPermission, cancellationToken);

        if (!permitted.IsGranted)
        {
            return Result.Failure(AuthorizationErrors.DelegationNotPermitted);
        }

        // A disabled account, or one that never existed. Minting a token naming
        // it would put a person into the audit trail who had nothing to do with
        // the call — and would route straight around a lockout, which is a live
        // suspicion that the account is under attack.
        if (!await delegationSubjects.CanActForAsync(onBehalfOfUserId, cancellationToken))
        {
            return Result.Failure(AuthorizationErrors.DelegationSubjectNotFound);
        }

        return Result.Success();
    }
}
