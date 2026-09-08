namespace CCP.Modules.Authorization.Domain.Scopes;

/// <summary>
/// The answer to "may this caller do this, and to what".
/// <para>
/// <b>It returns a filter, not a boolean</b> (ADR-007 §14.3). An authorization
/// system that answers only yes or no relies on every caller remembering to
/// apply the right restriction afterwards — and eventually one will not, and it
/// will return the whole company's records to someone entitled to see one
/// department. Handing back the filter makes the restriction the answer rather
/// than an obligation.
/// </para>
/// </summary>
public sealed record AccessDecision
{
    private AccessDecision(bool isGranted, ScopeType scope, IReadOnlyList<string> unitPathPrefixes)
    {
        IsGranted = isGranted;
        Scope = scope;
        UnitPathPrefixes = unitPathPrefixes;
    }

    public bool IsGranted { get; }

    /// <summary>The widest scope the caller holds for this permission.</summary>
    public ScopeType Scope { get; }

    /// <summary>
    /// Materialized-path prefixes the caller may reach.
    /// <para>
    /// A query applies these as <c>path LIKE prefix || '%'</c> — the indexed
    /// prefix scan Phase 3 built the path for. Empty when the scope is
    /// <see cref="ScopeType.All"/> (no restriction) or
    /// <see cref="ScopeType.Self"/> (restricted by identity, not by unit).
    /// </para>
    /// </summary>
    public IReadOnlyList<string> UnitPathPrefixes { get; }

    /// <summary>Denied. Carries no filter, because there is nothing to filter.</summary>
    public static AccessDecision Denied { get; } = new(false, ScopeType.Self, []);

    /// <summary>Granted company-wide. No unit restriction applies.</summary>
    public static AccessDecision GrantedForAll() => new(true, ScopeType.All, []);

    /// <summary>Granted for the caller's own records only.</summary>
    public static AccessDecision GrantedForSelf() => new(true, ScopeType.Self, []);

    /// <summary>Granted over the given unit subtrees.</summary>
    public static AccessDecision GrantedForUnits(ScopeType scope, IReadOnlyList<string> pathPrefixes)
        => new(true, scope, pathPrefixes);

    /// <summary>
    /// Whether a record belonging to a unit with the given path is within reach.
    /// <para>
    /// The in-memory counterpart of the SQL filter, for callers holding a single
    /// record rather than composing a query.
    /// </para>
    /// </summary>
    public bool Covers(string unitPath)
    {
        if (!IsGranted)
        {
            return false;
        }

        if (Scope == ScopeType.All)
        {
            return true;
        }

        if (Scope == ScopeType.Self)
        {
            // Self is restricted by identity, not by unit. A caller with Self
            // scope must check ownership themselves; answering true here would
            // silently widen it to the whole unit.
            return false;
        }

        foreach (string prefix in UnitPathPrefixes)
        {
            if (Scope == ScopeType.UnitAndBelow)
            {
                if (unitPath.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            else if (string.Equals(unitPath, prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
