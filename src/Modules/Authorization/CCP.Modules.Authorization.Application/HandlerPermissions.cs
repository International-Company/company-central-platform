namespace CCP.Modules.Authorization.Application;

/// <summary>
/// Permissions checked inside a handler rather than in front of a route.
/// <para>
/// The Platform's permission catalogue is derived from endpoint metadata, so a
/// permission exists only if some endpoint declares it. That is what stops the
/// list drifting — and it is also why a permission evaluated <i>inside</i> a
/// handler is never created, never granted to the administrator role, and can
/// never be held by anybody. Every check against it answers "denied".
/// </para>
/// <para>
/// <b>That happened.</b> Checking somebody else's access requires
/// <see cref="InspectOthersAccess"/>, evaluated in the handler because checking
/// one's own needs no permission at all. The seeder's list of exceptions was a
/// private array of literals holding only the delegation permission, so the
/// inspect permission was never catalogued, and the endpoint answered 403 to
/// everyone who asked about another user — the first administrator included —
/// while the documentation described it as working.
/// </para>
/// <para>
/// Handlers and the seeder now read this one list, and
/// <c>HandlerPermissionTests</c> fails the build on a handler that evaluates a
/// permission by literal instead of from here.
/// </para>
/// </summary>
public static class HandlerPermissions
{
    /// <summary>
    /// An application acting as a named person.
    /// <para>
    /// Checked by the token endpoint, which is anonymous — it is where a caller
    /// with no token gets one — so the requirement is evaluated against the
    /// application's own grants after its credentials have been verified.
    /// </para>
    /// </summary>
    public const string ActOnBehalf = "platform.applications.act-on-behalf";

    /// <summary>
    /// Asking what access another user holds.
    /// <para>
    /// Checked in the handler, not on the endpoint: the same endpoint answers a
    /// caller's question about themselves with no permission at all.
    /// </para>
    /// </summary>
    public const string InspectOthersAccess = "platform.authorization.inspect";

    /// <summary>Every permission above, for the seeder to catalogue.</summary>
    public static IReadOnlyList<string> All { get; } = [ActOnBehalf, InspectOthersAccess];
}
