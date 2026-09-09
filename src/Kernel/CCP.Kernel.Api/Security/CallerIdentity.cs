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
    /// <summary>Claim naming the kind of subject a token carries.</summary>
    public const string SubjectTypeClaim = "sub_type";

    /// <summary>Value of <see cref="SubjectTypeClaim"/> for a registered application.</summary>
    public const string ApplicationSubject = "application";

    /// <summary>Claim carrying the calling application's identifier.</summary>
    public const string ApplicationIdClaim = "app_id";

    /// <summary>Claim carrying the calling application's namespace code.</summary>
    public const string ApplicationCodeClaim = "app";

    /// <summary>
    /// The person making this request, or false when there is not one.
    /// <para>
    /// <b>A machine token acting as itself returns false, and that is the whole
    /// point of the check below.</b> Its subject claim holds an application id,
    /// which is a <c>Guid</c> and would parse perfectly — and every handler in
    /// the Platform that says "the caller's own records" would then treat an
    /// application as a person with that id. Refusing here means an endpoint
    /// written for people answers 401 to a machine instead of quietly operating
    /// on somebody's data.
    /// </para>
    /// <para>
    /// A delegated token — an application acting on behalf of somebody — does
    /// carry a person, and returns them. That is the difference between "this
    /// application is calling" and "this application is calling for Sara".
    /// </para>
    /// </summary>
    public static bool TryGetUserId(ClaimsPrincipal? principal, out Guid userId)
    {
        userId = Guid.Empty;

        if (IsApplicationSubject(principal))
        {
            return false;
        }

        string? subject = principal?.FindFirst("sub")?.Value
                       ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out userId);
    }

    /// <summary>
    /// The registered application making this request, when one is.
    /// <para>
    /// Set for every machine token, whether or not it is acting for somebody. An
    /// application acting on behalf of a person is still the application that
    /// called, and the audit trail records both.
    /// </para>
    /// </summary>
    public static bool TryGetApplicationId(ClaimsPrincipal? principal, out Guid applicationId)
    {
        applicationId = Guid.Empty;

        return Guid.TryParse(principal?.FindFirst(ApplicationIdClaim)?.Value, out applicationId);
    }

    /// <summary>Whether a machine is calling, in any form.</summary>
    public static bool IsMachineCaller(ClaimsPrincipal? principal)
        => principal?.FindFirst(ApplicationIdClaim) is not null;

    /// <summary>Whether the subject of this token is an application rather than a person.</summary>
    private static bool IsApplicationSubject(ClaimsPrincipal? principal)
        => string.Equals(
            principal?.FindFirst(SubjectTypeClaim)?.Value,
            ApplicationSubject,
            StringComparison.Ordinal);

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
