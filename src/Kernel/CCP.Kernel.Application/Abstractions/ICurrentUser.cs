namespace CCP.Kernel.Application.Abstractions;

/// <summary>
/// The caller of the current request.
/// <para>
/// The Application layer needs to know who is acting — for auditing and for
/// authorization — without depending on ASP.NET Core. This interface is that
/// seam; the API layer implements it from the HTTP context.
/// </para>
/// </summary>
public interface ICurrentUser
{
    /// <summary>The authenticated user, or null when the request is anonymous.</summary>
    Guid? UserId { get; }

    /// <summary>
    /// Username as it was at authentication time. Recorded alongside the ID in
    /// audit events so the trail stays readable after a rename
    /// (ARCHITECTURE.md §15.2).
    /// </summary>
    string? Username { get; }

    /// <summary>
    /// The application making the request. Null for a direct browser session;
    /// set when a registered business application is calling (ADR-012).
    /// </summary>
    string? ApplicationId { get; }

    /// <summary>The session this request belongs to, when there is one.</summary>
    Guid? SessionId { get; }

    bool IsAuthenticated { get; }
}
