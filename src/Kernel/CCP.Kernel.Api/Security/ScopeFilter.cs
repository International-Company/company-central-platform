using Microsoft.AspNetCore.Http;

namespace CCP.Kernel.Api.Security;

/// <summary>
/// How far a caller's granted access reaches, in a form any module's endpoint
/// can apply to its own query.
/// <para>
/// <b>Why this lives in the kernel.</b> The Authorization module decides the
/// filter, and every other module's endpoints have to apply it. Passing
/// Authorization's own decision type would mean Identity and Organization
/// referencing Authorization's internals, which §6.2 forbids. A neutral
/// cross-cutting shape — like the correlation id — is the right home for
/// something every module consumes and none owns.
/// </para>
/// <para>
/// Without this, authorization would decide <i>whether</i> a caller may act and
/// nothing would enforce <i>which</i> records they may act on. A caller with
/// department-level access would pass the permission check and then be handed
/// the whole company.
/// </para>
/// </summary>
/// <param name="Kind">How far the access reaches.</param>
/// <param name="UnitPathPrefixes">
/// Organizational path prefixes the caller may reach. A query applies these as
/// <c>path LIKE prefix || '%'</c>. Empty for <see cref="ScopeFilterKind.All"/>
/// and <see cref="ScopeFilterKind.Self"/>.
/// </param>
public sealed record ScopeFilter(ScopeFilterKind Kind, IReadOnlyList<string> UnitPathPrefixes)
{
    /// <summary>Key under which the filter travels on the request.</summary>
    public const string HttpContextKey = "ccp.authz.scope-filter";

    /// <summary>No restriction.</summary>
    public static ScopeFilter Unrestricted { get; } = new(ScopeFilterKind.All, []);

    /// <summary>
    /// The most restrictive filter. Used as the default when no filter was
    /// attached, so a missing filter denies rather than permits.
    /// </summary>
    public static ScopeFilter SelfOnly { get; } = new(ScopeFilterKind.Self, []);

    /// <summary>Whether the filter restricts by organizational unit at all.</summary>
    public bool RestrictsByUnit =>
        Kind is ScopeFilterKind.Unit or ScopeFilterKind.UnitAndBelow && UnitPathPrefixes.Count > 0;
}

/// <summary>How far a scope filter reaches. Mirrors the Authorization module's scope types.</summary>
public enum ScopeFilterKind
{
    /// <summary>Only the caller's own records.</summary>
    Self = 1,

    /// <summary>The named units exactly, not their descendants.</summary>
    Unit = 2,

    /// <summary>The named units and everything beneath them.</summary>
    UnitAndBelow = 3,

    /// <summary>Everything.</summary>
    All = 4
}

/// <summary>Reads the scope filter an authorized request carries.</summary>
public static class ScopeFilterExtensions
{
    /// <summary>
    /// The filter attached by the authorization handler.
    /// <para>
    /// <b>Defaults to <see cref="ScopeFilter.SelfOnly"/> when none is present.</b>
    /// A missing filter means either the endpoint is not permission-protected or
    /// something went wrong; in both cases the safe reading is "as little as
    /// possible". Defaulting to unrestricted would turn any wiring mistake into
    /// a data leak.
    /// </para>
    /// </summary>
    public static ScopeFilter GetScopeFilter(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Items.TryGetValue(ScopeFilter.HttpContextKey, out object? value)
               && value is ScopeFilter filter
            ? filter
            : ScopeFilter.SelfOnly;
    }
}
