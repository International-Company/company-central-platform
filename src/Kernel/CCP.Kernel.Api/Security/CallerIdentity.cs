using System.Security.Claims;

namespace CCP.Kernel.Api.Security;

/// <summary>
/// Who is making this request, read from the token in one place.
/// <para>
/// <b>There were three copies of this and one of them was wrong.</b> Two read
/// <c>sub</c> and fell back to <see cref="ClaimTypes.NameIdentifier"/>; the
/// third read only <c>sub</c>. That third one returned "no caller" for every
/// authenticated request ever made, and the endpoints using it answered 401 to
/// people holding perfectly good tokens: reading one's own permissions, granting
/// a role, revoking one. The administration portal hid every control it has,
/// because it could not discover that the administrator was allowed to see them.
/// </para>
/// <para>
/// The fallback is needed because ASP.NET Core's JWT handler renames inbound
/// claims by default: <c>sub</c> arrives as the WS-Federation URI
/// <c>http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier</c>,
/// so <c>FindFirst("sub")</c> finds nothing. The host now turns that mapping off
/// — a claim should be called what the token calls it — and the fallback stays
/// for any token minted before that change, and for any host that forgets.
/// </para>
/// <para>
/// One implementation, because the difference between the two versions was
/// invisible at every call site: both looked correct, both compiled, and only
/// one worked.
/// </para>
/// </summary>
public static class CallerIdentity
{
    /// <summary>The subject claim, however the pipeline chose to name it.</summary>
    public static bool TryGetUserId(ClaimsPrincipal? principal, out Guid userId)
    {
        userId = Guid.Empty;

        string? subject = principal?.FindFirst("sub")?.Value
                       ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out userId);
    }

    /// <summary>
    /// The subject and the session together.
    /// <para>
    /// Both or neither: an operation that needs to know which session is acting
    /// — ending it, keeping it alive while revoking the others — cannot proceed
    /// on half the answer.
    /// </para>
    /// </summary>
    public static bool TryGetIdentity(ClaimsPrincipal? principal, out Guid userId, out Guid sessionId)
    {
        sessionId = Guid.Empty;

        return TryGetUserId(principal, out userId)
            && Guid.TryParse(principal?.FindFirst("sid")?.Value, out sessionId);
    }
}
