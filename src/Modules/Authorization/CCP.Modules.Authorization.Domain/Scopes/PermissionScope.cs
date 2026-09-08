namespace CCP.Modules.Authorization.Domain.Scopes;

/// <summary>
/// How far a granted permission reaches.
/// <para>
/// <b>"View employees" is meaningless without asking <i>which</i> employees.</b>
/// Flat RBAC cannot express that, and full ABAC answers far more than anyone
/// asked at a cost paid on every request. Scope is the one extra dimension that
/// sits exactly on the requirement (ADR-007 §14.3).
/// </para>
/// <para>
/// Every value here is <i>organizational</i>, never business-conditional. There
/// is no "only records under 5,000" — that would be a business rule, and it
/// would put the Platform back inside the boundary ADR-005 draws.
/// </para>
/// </summary>
public enum ScopeType
{
    /// <summary>Only the holder's own records.</summary>
    Self = 1,

    /// <summary>One organizational unit, and only that unit.</summary>
    Unit = 2,

    /// <summary>
    /// A unit and everything beneath it. Resolved through the materialized path
    /// built in Phase 3, which is why this is a prefix scan rather than a tree
    /// walk on every request.
    /// </summary>
    UnitAndBelow = 3,

    /// <summary>Company-wide.</summary>
    All = 4
}

/// <summary>
/// A scope as granted: a kind, and optionally the unit it is anchored to.
/// <para>
/// <b>The anchor is what makes this useful.</b> When
/// <see cref="AnchorUnitId"/> is null, a unit scope follows the holder — "your
/// own department, wherever you are". When it is set, the scope is fixed to that
/// unit regardless of where the holder works: "Ahmad may see the Gaza branch",
/// even though Ahmad sits in head office.
/// </para>
/// <para>
/// Without the anchor, delegating oversight of one part of the company would
/// require moving the person into it.
/// </para>
/// </summary>
/// <param name="Type">How far the permission reaches.</param>
/// <param name="AnchorUnitId">
/// The unit the scope is fixed to, or null to follow the holder's own unit.
/// Meaningless for <see cref="ScopeType.Self"/> and <see cref="ScopeType.All"/>.
/// </param>
public sealed record GrantedScope(ScopeType Type, Guid? AnchorUnitId)
{
    /// <summary>Company-wide access.</summary>
    public static GrantedScope All { get; } = new(ScopeType.All, null);

    /// <summary>The holder's own records only.</summary>
    public static GrantedScope Self { get; } = new(ScopeType.Self, null);

    /// <summary>The holder's own unit, whichever that is at evaluation time.</summary>
    public static GrantedScope OwnUnit { get; } = new(ScopeType.Unit, null);

    /// <summary>The holder's own unit and everything beneath it.</summary>
    public static GrantedScope OwnUnitAndBelow { get; } = new(ScopeType.UnitAndBelow, null);

    /// <summary>A specific unit, regardless of where the holder works.</summary>
    public static GrantedScope UnitAnchoredAt(Guid unitId) => new(ScopeType.Unit, unitId);

    /// <summary>A specific unit and everything beneath it.</summary>
    public static GrantedScope SubtreeAnchoredAt(Guid unitId) => new(ScopeType.UnitAndBelow, unitId);

    /// <summary>
    /// Which of two scopes reaches further. Used when a user holds the same
    /// permission through several roles: the widest wins, because refusing
    /// access a person has genuinely been granted elsewhere would be wrong.
    /// </summary>
    public bool IsWiderThan(GrantedScope other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Type > other.Type;
    }
}
