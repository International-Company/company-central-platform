using System.Security.Claims;
using CCP.Kernel.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace CCP.Kernel.Api.Context;

/// <summary>
/// Reads the caller from the access token.
/// <para>
/// The Application layer needs to know who is acting — for auditing above all —
/// without depending on ASP.NET Core. <see cref="ICurrentUser"/> is that seam,
/// declared in Phase 1; this is the implementation it went without until audit
/// needed an actor to attribute events to.
/// </para>
/// <para>
/// Everything here comes from the validated token, never from a header or a
/// request body. An actor a caller could assert is an actor a caller could
/// forge, and an audit trail whose actor field can be chosen by the person being
/// audited is worse than none — it carries the authority of a record while
/// being fiction.
/// </para>
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            string? subject = Principal?.FindFirst("sub")?.Value
                           ?? Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return Guid.TryParse(subject, out Guid id) ? id : null;
        }
    }

    /// <summary>
    /// The username as it stood at authentication.
    /// <para>
    /// Recorded beside the id so the trail stays readable after a rename
    /// (ARCHITECTURE.md §15.2). A record that reads "user 8f3a… did X" once an
    /// employee's details change is not usable evidence.
    /// </para>
    /// </summary>
    public string? Username => Principal?.FindFirst("username")?.Value;

    /// <summary>
    /// The registered application calling, when one is. Null for a person
    /// signed in through the Platform's own API, which is the Platform acting.
    /// </summary>
    public string? ApplicationId
        => Principal?.FindFirst("client_id")?.Value ?? Principal?.FindFirst("azp")?.Value;

    public Guid? SessionId
        => Guid.TryParse(Principal?.FindFirst("sid")?.Value, out Guid id) ? id : null;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
}
