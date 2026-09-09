using CCP.Modules.Authorization.Application.Abstractions;
using CCP.Modules.Identity.Contracts;

namespace CCP.Modules.Authorization.Infrastructure;

/// <summary>
/// Issues machine tokens through Identity, which owns the signing key.
/// <para>
/// A thin adapter, and the thinness is the point: Authorization decides
/// <i>whether</i> a token should be issued and to whom; Identity decides what a
/// Platform token looks like and signs it. Neither knows the other's business,
/// and there is one token format in the company rather than two.
/// </para>
/// <para>
/// Lives in the infrastructure layer because it references another module's
/// contract, which is the composition root's business rather than the
/// application layer's (§6.2).
/// </para>
/// </summary>
public sealed class PlatformMachineTokenIssuer(IPlatformTokenMinter minter) : IMachineTokenIssuer
{
    public (string Token, TimeSpan Lifetime) IssueMachineToken(
        Guid applicationId,
        string applicationCode,
        string clientId,
        Guid? onBehalfOfUserId,
        DateTimeOffset now)
    {
        string token = minter.MintMachineToken(new MachineTokenRequest(
            applicationId, applicationCode, clientId, onBehalfOfUserId, now));

        return (token, minter.AccessTokenLifetime);
    }
}

/// <summary>
/// Confirms that a person an application wants to act for can sign in.
/// <para>
/// Through Identity's public surface, which answers a single boolean. A caller
/// that could tell "disabled" from "never existed" would have an
/// account-enumeration oracle reachable by anything holding a client credential.
/// </para>
/// </summary>
public sealed class PlatformDelegationSubjectVerifier(IUserDirectory users)
    : IDelegationSubjectVerifier
{
    public Task<bool> CanActForAsync(Guid userId, CancellationToken cancellationToken = default)
        => users.CanSignInAsync(userId, cancellationToken);
}
