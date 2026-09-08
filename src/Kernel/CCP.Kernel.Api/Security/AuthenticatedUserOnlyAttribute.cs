namespace CCP.Kernel.Api.Security;

/// <summary>
/// Declares that an endpoint requires an authenticated caller but no particular
/// permission.
/// <para>
/// A small, deliberately narrow category: operations any signed-in user may
/// perform <b>on their own account</b> — ending their own session, reading their
/// own profile, listing their own sessions. Gating those behind a permission
/// would mean granting it to literally everyone, which makes the permission
/// meaningless and the authorization model harder to read.
/// </para>
/// <para>
/// This exists so that "authenticated, no permission" is stated rather than
/// implied. The architecture test accepts <see cref="RequirePermissionAttribute"/>,
/// <c>AllowAnonymous</c>, or this — and nothing else. Silence is never a valid
/// access policy (P6, P9).
/// </para>
/// <para>
/// It carries no enforcement of its own. Authentication is enforced by the
/// framework's authorization policy; this records the intent so the endpoint's
/// access rule is visible at the call site and checkable by a test.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AuthenticatedUserOnlyAttribute : Attribute
{
    /// <summary>
    /// Why no permission is required. Recorded so the exemption is justified at
    /// the point it is taken, not argued about later.
    /// </summary>
    public AuthenticatedUserOnlyAttribute(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    public string Reason { get; }
}
