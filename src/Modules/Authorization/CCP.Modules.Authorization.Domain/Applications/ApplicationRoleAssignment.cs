using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Authorization.Domain.Scopes;

namespace CCP.Modules.Authorization.Domain.Applications;

/// <summary>
/// A role granted to an application rather than to a person.
/// <para>
/// <b>The same roles, the same permissions, the same scope model.</b> An
/// application that may read employees holds the role that says so, at a scope,
/// and the resolver computes its effective permissions through exactly the join
/// it uses for a person. Nothing about permission evaluation is duplicated for
/// machines.
/// </para>
/// <para>
/// A separate table rather than a nullable column on the user grant, because the
/// two answer different questions and are read by different code paths. Sharing
/// one table would mean every query about people carrying a filter to exclude
/// machines, and the first query that forgot it would be a real defect.
/// </para>
/// <para>
/// There is deliberately no separate vocabulary of "API scopes". A second
/// permission language would have to be kept in step with the first, and the day
/// they disagreed nobody would know which one was authoritative.
/// </para>
/// </summary>
public sealed class ApplicationRoleAssignment : AggregateRoot
{
    private ApplicationRoleAssignment() { }

    private ApplicationRoleAssignment(
        Guid id,
        Guid applicationId,
        Guid roleId,
        GrantedScope scope,
        Guid grantedBy,
        DateTimeOffset now,
        DateTimeOffset? expiresAt)
        : base(id)
    {
        ApplicationId = applicationId;
        RoleId = roleId;
        ScopeType = scope.Type;
        ScopeUnitId = scope.AnchorUnitId;
        GrantedBy = grantedBy;
        GrantedAt = now;
        ExpiresAt = expiresAt;
    }

    public Guid ApplicationId { get; private set; }

    public Guid RoleId { get; private set; }

    public ScopeType ScopeType { get; private set; }

    public Guid? ScopeUnitId { get; private set; }

    public Guid GrantedBy { get; private set; }

    public DateTimeOffset GrantedAt { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? RevokedBy { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public GrantedScope Scope => new(ScopeType, ScopeUnitId);

    public bool IsEffective(DateTimeOffset now) =>
        RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);

    public static Result<ApplicationRoleAssignment> Grant(
        Guid applicationId,
        Guid roleId,
        GrantedScope scope,
        Guid grantedBy,
        DateTimeOffset now,
        DateTimeOffset? expiresAt = null)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (expiresAt is { } expiry && expiry <= now)
        {
            return Result.Failure<ApplicationRoleAssignment>(AuthorizationErrors.ExpiryInThePast);
        }

        // An application has no place in the organization, so a scope that
        // follows the holder has nothing to follow. It has to be anchored or
        // company-wide, and Self means nothing at all here.
        if (scope.Type == ScopeType.Self)
        {
            return Result.Failure<ApplicationRoleAssignment>(
                AuthorizationErrors.SelfScopeMeaninglessForApplication);
        }

        if (scope.Type is ScopeType.Unit or ScopeType.UnitAndBelow && scope.AnchorUnitId is null)
        {
            return Result.Failure<ApplicationRoleAssignment>(AuthorizationErrors.ScopeUnitRequired);
        }

        if (scope.Type == ScopeType.All && scope.AnchorUnitId is not null)
        {
            return Result.Failure<ApplicationRoleAssignment>(AuthorizationErrors.ScopeUnitNotApplicable);
        }

        return Result.Success(new ApplicationRoleAssignment(
            Uuid7.NewGuid(now), applicationId, roleId, scope, grantedBy, now, expiresAt));
    }

    public Result Revoke(Guid revokedBy, DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return Result.Failure(AuthorizationErrors.AssignmentAlreadyRevoked);
        }

        RevokedAt = now;
        RevokedBy = revokedBy;

        return Result.Success();
    }
}
