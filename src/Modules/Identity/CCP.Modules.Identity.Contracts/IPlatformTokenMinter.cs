namespace CCP.Modules.Identity.Contracts;

/// <summary>
/// Mints the tokens that are not a person signing in.
/// <para>
/// <b>Identity owns the token format, and there is exactly one of it.</b> A
/// machine token and a user token are signed by the same key, carry the same
/// issuer and audience, and are validated by the same middleware — which is what
/// lets a business application verify either one against the published JWKS
/// without knowing which it got.
/// </para>
/// <para>
/// The alternative was letting Authorization sign its own tokens. That would
/// mean a second key, a second lifetime, a second set of validation parameters,
/// and two places to get token security wrong instead of one.
/// </para>
/// <para>
/// Kept to the one shape another module actually needs. There is no method here
/// that mints a token for an arbitrary subject with arbitrary claims: that is a
/// forgery primitive, and an interface offering it would eventually be called by
/// something that should not have.
/// </para>
/// </summary>
public interface IPlatformTokenMinter
{
    /// <summary>How long an issued token is good for.</summary>
    TimeSpan AccessTokenLifetime { get; }

    /// <summary>Issues a token for a registered application.</summary>
    string MintMachineToken(MachineTokenRequest request);
}

/// <summary>
/// Everything that goes into a machine token.
/// </summary>
/// <param name="ApplicationId">The calling application.</param>
/// <param name="ApplicationCode">Its namespace, so a reader can see who called.</param>
/// <param name="ClientId">
/// The credential used. Present so a leaked token can be traced to one key, and
/// so revoking that key is a decision somebody can make with evidence.
/// </param>
/// <param name="OnBehalfOfUserId">
/// The person the application is acting for, or null when it acts as itself.
/// <b>When set, both identities are in the token</b> — the subject is the
/// person and the client is the application — because "which application, on
/// behalf of whom" is the question audit exists to answer (§13.4).
/// </param>
/// <param name="IssuedAt">The moment of issue, from the Platform clock.</param>
public sealed record MachineTokenRequest(
    Guid ApplicationId,
    string ApplicationCode,
    string ClientId,
    Guid? OnBehalfOfUserId,
    DateTimeOffset IssuedAt);
