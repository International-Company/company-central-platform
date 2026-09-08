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
    IOrganizationScopeReader organizationReader,
    IPermissionVersionStore versionStore,
    IEffectivePermissionCache cache,
    IClock clock) : IPermissionResolver
{
    public async Task<EffectivePermissions> GetEffectivePermissionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        long currentVersion = await versionStore.GetCurrentAsync(cancellationToken);

        // A cached set computed at an older stamp is stale, and stale here means
        // possibly granting revoked access. Discard rather than use.
        if (cache.TryGet(userId, out EffectivePermissions? cached)
            && cached.Version == currentVersion)
        {
            return cached;
        }

        EffectivePermissions resolved = await ComputeAsync(userId, currentVersion, cancellationToken);

        cache.Set(resolved);

        return resolved;
    }

    public Task<string?> GetUserUnitPathAsync(Guid userId, CancellationToken cancellationToken = default)
        => organizationReader.GetUnitPathForUserAsync(userId, cancellationToken);

    public async Task<AccessDecision> EvaluateAsync(
        Guid userId,
        string permissionName,
        CancellationToken cancellationToken = default)
    {
        EffectivePermissions permissions =
            await GetEffectivePermissionsAsync(userId, cancellationToken);

        // Only fetched when something actually needs it. A user whose grants are
        // all All-scoped or anchored never triggers this lookup.
        string? callerUnitPath = null;

        if (NeedsCallerUnit(permissions))
        {
            callerUnitPath = await organizationReader.GetUnitPathForUserAsync(userId, cancellationToken);
        }

        return permissions.Evaluate(permissionName, callerUnitPath);
    }

    private async Task<EffectivePermissions> ComputeAsync(
        Guid userId,
        long version,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<GrantRow> grants =
            await repository.GetGrantsForUserAsync(userId, clock.UtcNow, cancellationToken);

        if (grants.Count == 0)
        {
            return EffectivePermissions.None(userId, version);
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

        return new EffectivePermissions(userId, version, held);
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
    bool TryGet(Guid userId, [NotNullWhen(true)] out EffectivePermissions? permissions);

    void Set(EffectivePermissions permissions);

    /// <summary>
    /// Drops one user's entry. An optimisation for the common case of revoking
    /// one person's role; correctness comes from the version stamp regardless.
    /// </summary>
    void Invalidate(Guid userId);
}
