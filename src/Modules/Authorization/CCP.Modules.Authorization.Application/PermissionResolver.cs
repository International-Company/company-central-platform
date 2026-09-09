using System.Diagnostics.CodeAnalysis;
using CCP.Kernel.Primitives;
using CCP.Modules.Authorization.Application.Abstractions;
using CCP.Modules.Authorization.Domain.Scopes;

namespace CCP.Modules.Authorization.Application;

/// <summary>
/// Resolves a user's effective permissions, with a version-stamped cache.
/// <para>
/// <b>The correctness property that matters:</b> revoking a role must take
/// effect on the very next request. That is why the cache is keyed on a stamp
/// held in the database rather than a time-to-live. A TTL cache would leave a
/// window — however short — in which someone keeps access that was deliberately
/// taken away, and "however short" is not a property anyone can reason about
/// during an incident.
/// </para>
/// <para>
/// The cost is one indexed single-row read per request to fetch the current
/// stamp. That is far cheaper than recomputing the permission join every time,
/// and unlike a TTL it is exactly correct.
/// </para>
/// </summary>
public sealed class PermissionResolver(
    IAuthorizationRepository repository,
    IApplicationRepository applications,
    IOrganizationScopeReader organizationReader,
    IPermissionVersionStore versionStore,
    IEffectivePermissionCache cache,
    IClock clock) : IPermissionResolver
{
    public Task<EffectivePermissions> GetEffectivePermissionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
        => GetAsync(PermissionSubject.ForUser(userId), cancellationToken);

    public Task<EffectivePermissions> GetEffectivePermissionsForApplicationAsync(
        Guid applicationId,
        CancellationToken cancellationToken = default)
        => GetAsync(PermissionSubject.ForApplication(applicationId), cancellationToken);

    private async Task<EffectivePermissions> GetAsync(
        PermissionSubject subject,
        CancellationToken cancellationToken)
    {
        long currentVersion = await versionStore.GetCurrentAsync(cancellationToken);

        // A cached set computed at an older stamp is stale, and stale here means
        // possibly granting revoked access. Discard rather than use.
        if (cache.TryGet(subject, out EffectivePermissions? cached)
            && cached.Version == currentVersion)
        {
            return cached;
        }

        EffectivePermissions resolved = await ComputeAsync(subject, currentVersion, cancellationToken);

        cache.Set(resolved);

        return resolved;
    }

    public Task<string?> GetUserUnitPathAsync(Guid userId, CancellationToken cancellationToken = default)
        => organizationReader.GetUnitPathForUserAsync(userId, cancellationToken);

    public Task<AccessDecision> EvaluateAsync(
        Guid userId,
        string permissionName,
        CancellationToken cancellationToken = default)
        => EvaluateForAsync(PermissionSubject.ForUser(userId), permissionName, cancellationToken);

    public Task<AccessDecision> EvaluateForApplicationAsync(
        Guid applicationId,
        string permissionName,
        CancellationToken cancellationToken = default)
        => EvaluateForAsync(
            PermissionSubject.ForApplication(applicationId), permissionName, cancellationToken);

    /// <summary>
    /// An application acting for a person, which is the narrower of the two.
    /// <para>
    /// <b>The intersection, deliberately.</b> A delegated call may do only what
    /// the application is trusted with <i>and</i> what the person is entitled to.
    /// Taking the user's rights alone would make every registered application a
    /// way to act as anybody it can name; taking the application's alone would
    /// let it reach data the person it claims to be acting for cannot see.
    /// </para>
    /// <para>
    /// The narrower scope wins as well as the narrower grant, so an application
    /// scoped to one department acting for somebody with company-wide access
    /// still reaches only that department.
    /// </para>
    /// </summary>
    public async Task<AccessDecision> EvaluateDelegatedAsync(
        Guid applicationId,
        Guid userId,
        string permissionName,
        CancellationToken cancellationToken = default)
    {
        AccessDecision application =
            await EvaluateForApplicationAsync(applicationId, permissionName, cancellationToken);

        if (!application.IsGranted)
        {
            return AccessDecision.Denied;
        }

        AccessDecision user = await EvaluateAsync(userId, permissionName, cancellationToken);

        if (!user.IsGranted)
        {
            return AccessDecision.Denied;
        }

        return Narrower(application, user);
    }

    private async Task<AccessDecision> EvaluateForAsync(
        PermissionSubject subject,
        string permissionName,
        CancellationToken cancellationToken)
    {
        EffectivePermissions permissions = await GetAsync(subject, cancellationToken);

        // Only fetched when something actually needs it. A caller whose grants
        // are all All-scoped or anchored never triggers this lookup — and an
        // application has no unit to follow at all.
        string? callerUnitPath = null;

        if (subject.HasPlaceInOrganization && NeedsCallerUnit(permissions))
        {
            callerUnitPath =
                await organizationReader.GetUnitPathForUserAsync(subject.Id, cancellationToken);
        }

        return permissions.Evaluate(permissionName, callerUnitPath);
    }

    /// <summary>
    /// The more restrictive of two grants of the same permission.
    /// <para>
    /// Company-wide loses to anything narrower. Between two unit-scoped
    /// decisions the reach is the intersection, which for materialized paths
    /// means keeping a prefix only when the other side already covers it.
    /// </para>
    /// </summary>
    private static AccessDecision Narrower(AccessDecision application, AccessDecision user)
    {
        if (application.Scope == ScopeType.All)
        {
            return user;
        }

        if (user.Scope == ScopeType.All)
        {
            return application;
        }

        if (application.Scope == ScopeType.Self || user.Scope == ScopeType.Self)
        {
            // Self means "the caller's own records", and an application has no
            // records of its own. A delegated call that comes down to Self is
            // the user's own data, checked by the handler that owns it.
            return AccessDecision.GrantedForSelf();
        }

        ScopeType scope = application.Scope < user.Scope ? application.Scope : user.Scope;

        List<string> reach = [.. application.UnitPathPrefixes.Where(user.Covers)];

        reach.AddRange(user.UnitPathPrefixes.Where(
            prefix => application.Covers(prefix) && !reach.Contains(prefix, StringComparer.Ordinal)));

        return reach.Count == 0
            ? AccessDecision.Denied
            : AccessDecision.GrantedForUnits(scope, reach);
    }

    private async Task<EffectivePermissions> ComputeAsync(
        PermissionSubject subject,
        long version,
        CancellationToken cancellationToken)
    {
        // The same join, from whichever grant table the subject lives in. Two
        // queries and one algorithm: a machine caller and a person are evaluated
        // by identical code, which is why there is no second set of rules to
        // keep in step with the first.
        IReadOnlyList<GrantRow> grants = subject.Kind == PermissionSubjectKind.Application
            ? await applications.GetGrantsForApplicationAsync(subject.Id, clock.UtcNow, cancellationToken)
            : await repository.GetGrantsForUserAsync(subject.Id, clock.UtcNow, cancellationToken);

        if (grants.Count == 0)
        {
            return EffectivePermissions.None(subject, version);
        }

        // Anchor unit ids are turned into paths once, here, so that evaluation —
        // which happens on every permission check — needs no lookups at all.
        var pathsByUnitId = new Dictionary<Guid, string?>();

        foreach (Guid unitId in grants.Where(g => g.ScopeUnitId is not null)
                                      .Select(g => g.ScopeUnitId!.Value)
                                      .Distinct())
        {
            pathsByUnitId[unitId] = await organizationReader.GetUnitPathAsync(unitId, cancellationToken);
        }

        var held = new List<HeldPermission>();

        foreach (IGrouping<string, GrantRow> group in grants.GroupBy(g => g.PermissionName, StringComparer.Ordinal))
        {
            var scopes = new List<ResolvedScope>();

            foreach (GrantRow grant in group)
            {
                string? unitPath = null;

                if (grant.ScopeUnitId is { } anchorId)
                {
                    unitPath = pathsByUnitId.GetValueOrDefault(anchorId);

                    // The anchor unit has been deleted or is unreadable. Skip the
                    // grant rather than falling back to the caller's own unit,
                    // which would silently move the scope somewhere it was never
                    // granted.
                    if (unitPath is null)
                    {
                        continue;
                    }
                }

                scopes.Add(new ResolvedScope(grant.ScopeType, unitPath));
            }

            if (scopes.Count > 0)
            {
                held.Add(new HeldPermission(group.Key, scopes));
            }
        }

        return new EffectivePermissions(subject, version, held);
    }

    /// <summary>
    /// Whether any held scope follows the caller rather than an anchor. Avoids
    /// an organizational lookup for the common case of All-scoped grants.
    /// </summary>
    private static bool NeedsCallerUnit(EffectivePermissions permissions)
    {
        foreach (string name in permissions.PermissionNames)
        {
            if (permissions.WidestScopeFor(name) is ScopeType.Unit or ScopeType.UnitAndBelow)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// An in-process cache of resolved permissions.
/// <para>
/// In-process rather than distributed, deliberately. Correctness does not depend
/// on instances agreeing — each checks the shared version stamp itself, so a
/// stale entry on one instance is detected there. A distributed cache would add
/// infrastructure to solve a problem the stamp already solves (P1).
/// </para>
/// </summary>
public interface IEffectivePermissionCache
{
    bool TryGet(PermissionSubject subject, [NotNullWhen(true)] out EffectivePermissions? permissions);

    void Set(EffectivePermissions permissions);

    /// <summary>
    /// Drops one user's entry. An optimisation for the common case of revoking
    /// one person's role; correctness comes from the version stamp regardless.
    /// </summary>
    void Invalidate(PermissionSubject subject);
}
