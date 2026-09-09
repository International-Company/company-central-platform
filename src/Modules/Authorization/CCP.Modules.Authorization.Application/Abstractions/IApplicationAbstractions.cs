using CCP.Modules.Authorization.Domain.Applications;
using CCP.Modules.Authorization.Domain.Scopes;

namespace CCP.Modules.Authorization.Application.Abstractions;

/// <summary>
/// Generates and verifies client secrets.
/// <para>
/// <b>SHA-256, not Argon2, and that deserves an explanation</b> because it looks
/// like the mistake everybody is taught to avoid.
/// </para>
/// <para>
/// Argon2 exists to make guessing expensive, and guessing is only worth
/// attempting against a secret a human chose. A client secret here is 32 bytes
/// from a cryptographic generator — there is no dictionary, no reuse from
/// another site, and no amount of hardware that makes 2^256 tractable. Slow
/// hashing buys nothing against that.
/// </para>
/// <para>
/// What it does cost is real: the token endpoint is on the path of every machine
/// call in the company, and a hash deliberately tuned to take 50 milliseconds
/// would make it the slowest thing in the Platform and a denial-of-service
/// amplifier — an attacker with no valid credential at all could spend the
/// server's CPU by presenting garbage.
/// </para>
/// <para>
/// The property that actually matters is preserved: the stored value cannot be
/// turned back into the secret, so a database disclosure does not hand anybody a
/// working credential.
/// </para>
/// </summary>
public interface IApplicationSecretHasher
{
    /// <summary>
    /// Mints a new credential pair. The plaintext is returned once, to be shown
    /// once, and is never stored anywhere.
    /// </summary>
    (string ClientId, string Secret, string SecretHash) Generate();

    /// <summary>Hashes a presented secret so it can be compared with a stored one.</summary>
    string Hash(string secret);

    /// <summary>
    /// Compares in constant time. An ordinary string comparison returns faster
    /// for a wrong first character than for a wrong last one, and that
    /// difference is measurable across enough requests.
    /// </summary>
    bool Matches(string presentedSecret, string storedHash);
}

/// <summary>
/// Persistence for application credentials and application grants.
/// <para>
/// Separate from <c>IAuthorizationRepository</c> only because that interface is
/// already long; both are implemented by the same class over the same schema.
/// </para>
/// </summary>
public interface IApplicationRepository
{
    // --- Credentials -------------------------------------------------------

    /// <summary>
    /// The credential bearing a client id, whether or not it is still live.
    /// <para>
    /// Revoked and expired credentials are returned rather than filtered out, so
    /// the caller can tell "this key was withdrawn" from "this key never
    /// existed" — a distinction worth having in the security log, and one the
    /// caller is never told.
    /// </para>
    /// </summary>
    Task<ApplicationCredential?> FindCredentialByClientIdAsync(
        string clientId, CancellationToken cancellationToken = default);

    Task<ApplicationCredential?> FindCredentialAsync(
        Guid credentialId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApplicationCredential>> GetCredentialsAsync(
        Guid applicationId, CancellationToken cancellationToken = default);

    void AddCredential(ApplicationCredential credential);

    // --- Grants ------------------------------------------------------------

    Task<ApplicationRoleAssignment?> FindApplicationAssignmentAsync(
        Guid assignmentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApplicationRoleAssignment>> GetAssignmentsForApplicationAsync(
        Guid applicationId, CancellationToken cancellationToken = default);

    Task<bool> ApplicationAssignmentExistsAsync(
        Guid applicationId, Guid roleId, ScopeType scopeType, Guid? scopeUnitId,
        CancellationToken cancellationToken = default);

    void AddApplicationAssignment(ApplicationRoleAssignment assignment);

    /// <summary>
    /// The permission join for an application — the same shape as the one for a
    /// user, from the other grant table.
    /// </summary>
    Task<IReadOnlyList<GrantRow>> GetGrantsForApplicationAsync(
        Guid applicationId, DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <summary>
/// Mints a machine access token.
/// <para>
/// Implemented outside this module. Authorization knows which application is
/// calling and what it may do; it does not know how a token is signed, and a
/// module that reached into Identity's key material to find out would not be
/// extractable (§6.2).
/// </para>
/// </summary>
public interface IMachineTokenIssuer
{
    /// <summary>
    /// Issues a token for an application, optionally acting for a user.
    /// </summary>
    /// <param name="applicationId">The calling application.</param>
    /// <param name="applicationCode">Its namespace, for the log and the claim.</param>
    /// <param name="clientId">The credential used, so a leak can be traced to one key.</param>
    /// <param name="onBehalfOfUserId">
    /// The person the application is acting for, or null when it acts as itself.
    /// <b>Both identities end up in the token</b>, because "which application, on
    /// behalf of whom" is the question audit has to answer (§13.4).
    /// </param>
    (string Token, TimeSpan Lifetime) IssueMachineToken(
        Guid applicationId,
        string applicationCode,
        string clientId,
        Guid? onBehalfOfUserId,
        DateTimeOffset now);
}

/// <summary>
/// Confirms that a user an application wants to act for can actually sign in.
/// <para>
/// Implemented over Identity's public surface. Without it, delegation would
/// happily mint a token for a disabled account, a deleted account, or a
/// <c>Guid</c> somebody typed wrong — and the resulting audit trail would name a
/// person who had nothing to do with it.
/// </para>
/// </summary>
public interface IDelegationSubjectVerifier
{
    Task<bool> CanActForAsync(Guid userId, CancellationToken cancellationToken = default);
}
