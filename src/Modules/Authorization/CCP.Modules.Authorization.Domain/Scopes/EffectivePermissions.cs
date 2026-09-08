namespace CCP.Modules.Authorization.Domain.Scopes;

/// <summary>
/// One permission a user holds, and the scopes they hold it at.
/// </summary>
/// <param name="PermissionName">The full permission name.</param>
/// <param name="Scopes">
/// Every scope the permission was granted at. A user can hold the same
/// permission through several roles — as a department head and as a member of an
/// audit team, say — and the widest wins.
/// </param>
public sealed record HeldPermission(string PermissionName, IReadOnlyList<ResolvedScope> Scopes);

/// <summary>
/// A scope after resolution: the anchor has been turned into a concrete unit
/// path, so evaluation needs no further lookups.
/// </summary>
/// <param name="Type">How far it reaches.</param>
/// <param name="UnitPath">
/// The materialized path of the anchor unit. Null for
/// <see cref="ScopeType.Self"/> and <see cref="ScopeType.All"/>, where no unit
/// is involved.
/// </param>
public sealed record ResolvedScope(ScopeType Type, string? UnitPath);

/// <summary>
/// Everything a user may do, resolved and ready to evaluate.
/// <para>
/// Built once per request (or served from cache when the permission version is
/// unchanged), then answered from memory. This is the type the
/// <c>RequirePermission</c> handler consults on <b>every request in the
/// company</b>, so lookup is a dictionary hit and nothing here touches a
/// database.
/// </para>
/// </summary>
public sealed class EffectivePermissions
{
    private readonly Dictionary<string, HeldPermission> _permissions;

    public EffectivePermissions(Guid userId, long version, IReadOnlyList<HeldPermission> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        UserId = userId;
        Version = version;
        _permissions = permissions.ToDictionary(p => p.PermissionName, StringComparer.Ordinal);
    }

    public Guid UserId { get; }

    /// <summary>
    /// The permission-version stamp these were computed at. A cached instance
    /// whose version no longer matches the current one is stale and is discarded
    /// rather than used (ADR-007 §14.4).
    /// </summary>
    public long Version { get; }

    public IReadOnlyCollection<string> PermissionNames => _permissions.Keys;

    /// <summary>An empty set, for an unauthenticated or unknown caller.</summary>
    public static EffectivePermissions None(Guid userId, long version)
        => new(userId, version, []);

    /// <summary>
    /// Evaluates a permission, returning both the decision and the filter that
    /// must be applied to the data.
    /// </summary>
    /// <param name="permissionName">The permission to check.</param>
    /// <param name="callerUnitPath">
    /// The caller's own unit path, used to resolve scopes that follow the
    /// holder. Null when the caller has no employee record — in which case
    /// holder-relative scopes cannot resolve and grant nothing.
    /// </param>
    public AccessDecision Evaluate(string permissionName, string? callerUnitPath)
    {
        if (!_permissions.TryGetValue(permissionName, out HeldPermission? held))
        {
            return AccessDecision.Denied;
        }

        ScopeType widest = ScopeType.Self;
        var prefixes = new List<string>();
        bool anyGranted = false;

        foreach (ResolvedScope scope in held.Scopes)
        {
            switch (scope.Type)
            {
                case ScopeType.All:
                    // Nothing beats company-wide, so stop looking.
                    return AccessDecision.GrantedForAll();

                case ScopeType.Self:
                    anyGranted = true;
                    break;

                case ScopeType.Unit:
                case ScopeType.UnitAndBelow:
                    {
                        string? path = scope.UnitPath ?? callerUnitPath;

                        // A holder-relative scope on a caller with no unit resolves
                        // to nothing. Treating it as "all" would be a catastrophic
                        // default; treating it as "self" would silently widen it.
                        if (path is null)
                        {
                            continue;
                        }

                        anyGranted = true;
                        prefixes.Add(path);

                        if (scope.Type > widest)
                        {
                            widest = scope.Type;
                        }

                        break;
                    }

                default:
                    continue;
            }
        }

        if (!anyGranted)
        {
            return AccessDecision.Denied;
        }

        return prefixes.Count == 0
            ? AccessDecision.GrantedForSelf()
            : AccessDecision.GrantedForUnits(widest, prefixes);
    }

    /// <summary>
    /// Whether the permission is held at all, at any scope. Used by the
    /// anti-escalation check, which asks whether a granter may hand something on
    /// — not what data they may see.
    /// </summary>
    public bool Holds(string permissionName) => _permissions.ContainsKey(permissionName);

    /// <summary>The widest scope held for a permission, or null if not held.</summary>
    public ScopeType? WidestScopeFor(string permissionName)
        => _permissions.TryGetValue(permissionName, out HeldPermission? held) && held.Scopes.Count > 0
            ? held.Scopes.Max(s => s.Type)
            : null;
}
