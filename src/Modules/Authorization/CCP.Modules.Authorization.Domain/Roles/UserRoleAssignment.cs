using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Authorization.Domain.Scopes;

namespace CCP.Modules.Authorization.Domain.Roles;

/// <summary>
/// A role granted to a user, at a scope, optionally for a limited time.
/// <para>
/// <b>Delegation is not a separate concept here.</b> The plan listed a
/// <c>DelegatedRole</c> entity; it turned out to be this one with an expiry.
/// "Cover for the finance manager until the 30th" is a role assignment that
/// stops working on the 30th — a second table would have duplicated every rule
/// about scope, escalation and auditing, and the two would have drifted.
/// </para>
/// </summary>
public sealed class UserRoleAssignment : AggregateRoot
{
    private UserRoleAssignment() { }

    private UserRoleAssignment(
        Guid id,
        Guid userId,
        Guid roleId,
        GrantedScope scope,
        Guid grantedBy,
        DateTimeOffset now,
        DateTimeOffset? expiresAt)
        : base(id)
    {
        UserId = userId;
        RoleId = roleId;
        ScopeType = scope.Type;
        ScopeUnitId = scope.AnchorUnitId;
        GrantedBy = grantedBy;
        GrantedAt = now;
        ExpiresAt = expiresAt;
    }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }

    public ScopeType ScopeType { get; private set; }

    /// <summary>
    /// The unit the scope is fixed to, or null to follow the holder's own unit.
    /// </summary>
    public Guid? ScopeUnitId { get; private set; }

    /// <summary>
    /// Who granted it. Recorded because "who gave this person access" is the
    /// first question asked after an incident, and reconstructing it from audit
    /// alone is slower than reading it here.
    /// </summary>
    public Guid GrantedBy { get; private set; }

    public DateTimeOffset GrantedAt { get; private set; }

    /// <summary>
    /// When the grant lapses. Null means indefinite.
    /// <para>
    /// A lapsed grant simply stops counting — no background job removes it,
    /// which is one fewer thing that can fail and leave someone holding access
    /// they should not.
    /// </para>
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? RevokedBy { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public GrantedScope Scope => new(ScopeType, ScopeUnitId);

    /// <summary>Whether this grant counts at the given moment.</summary>
    public bool IsEffective(DateTimeOffset now)
        => !IsRevoked && (ExpiresAt is not { } expiry || expiry > now);

    /// <summary>
    /// Grants a role.
    /// <para>
    /// <b>Two invariants are enforced by the caller, not here</b>, because this
    /// type cannot see the facts they depend on:
    /// </para>
    /// <list type="number">
    /// <item>No user may grant a permission they do not themselves hold — the
    /// granter's own effective permissions are needed to check that.</item>
    /// <item>No user may modify their own roles — checked here, since it needs
    /// only the two ids.</item>
    /// </list>
    /// </summary>
    public static Result<UserRoleAssignment> Grant(
        Guid userId,
        Guid roleId,
        GrantedScope scope,
        Guid grantedBy,
        DateTimeOffset now,
        DateTimeOffset? expiresAt = null)
    {
        ArgumentNullException.ThrowIfNull(scope);

        // Nobody grants themselves a role. Without this, anyone who reaches the
        // grant endpoint at all can escalate to anything.
        if (userId == grantedBy)
        {
            return Result.Failure<UserRoleAssignment>(AuthorizationErrors.CannotGrantToSelf);
        }

        if (expiresAt is { } expiry && expiry <= now)
        {
            return Result.Failure<UserRoleAssignment>(AuthorizationErrors.ExpiryInThePast);
        }

        // A unit-anchored scope needs a unit; an unanchored one follows the
        // holder. Neither is valid for Self or All, where a unit is meaningless.
        if (scope.Type is ScopeType.Self or ScopeType.All && scope.AnchorUnitId is not null)
        {
            return Result.Failure<UserRoleAssignment>(AuthorizationErrors.ScopeUnitNotApplicable);
        }

        return Result.Success(new UserRoleAssignment(
            Uuid7.NewGuid(now), userId, roleId, scope, grantedBy, now, expiresAt));
    }

    public Result Revoke(Guid revokedBy, DateTimeOffset now)
    {
        if (IsRevoked)
        {
            return Result.Success();
        }

        RevokedAt = now;
        RevokedBy = revokedBy;

        return Result.Success();
    }
}
