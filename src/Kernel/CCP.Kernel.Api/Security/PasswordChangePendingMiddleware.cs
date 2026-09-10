using System.Security.Claims;
using System.Text.Json;
using CCP.Kernel.Api.Context;
using CCP.Kernel.Api.Errors;
using CCP.Kernel.Application.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Kernel.Api.Security;

/// <summary>
/// Marks an endpoint a caller may still reach while they owe a password change.
/// <para>
/// The list has to be short and it has to be deliberate, so it is expressed as
/// metadata on the endpoints themselves rather than as a route allow-list in a
/// middleware — the same way every other access rule in the Platform is
/// expressed. A route allow-list drifts the moment somebody renames a path,
/// silently, in the direction of letting more through.
/// </para>
/// </summary>
/// <param name="Because">
/// Why this one is reachable. Written down because a reviewer looking at this
/// attribute is asking exactly that, and "it seemed necessary" is the answer
/// that lets the list grow.
/// </param>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AllowWhilePasswordChangePendingAttribute(string because) : Attribute
{
    public string Because { get; } = because;
}

/// <summary>
/// Stops a caller who owes a password change from doing anything else.
/// <para>
/// <b>The portal already redirected; nothing else did.</b> It reads
/// <c>mustChangePassword</c> from <c>/me</c> and sends the person to the change
/// screen, which covers everybody who uses a browser and covers nobody who
/// calls the API directly. So a temporary password issued by an administrator
/// was a working credential for the entire API until its holder happened to
/// open the portal — which is the opposite of what a forced change is for. It
/// exists so that the password only one other person knows stops working
/// quickly.
/// </para>
/// <para>
/// <b>The claim is in the token, so this costs no query.</b> Reading Identity on
/// every request to ask a question that was already settled at sign-in would put
/// a database round trip in front of the whole Platform. The consequence is that
/// the flag is as old as the token: somebody who changes their password keeps a
/// token that still says they owe one, until it expires or they refresh. Both
/// paths mint a fresh token from current state, and the lifetime is minutes, so
/// the window is small and closes by itself — and it errs towards asking again
/// rather than towards letting through.
/// </para>
/// </summary>
public sealed class PasswordChangePendingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!Owes(context.User) || IsPermitted(context))
        {
            await next(context);

            return;
        }

        var requestContext = context.RequestServices.GetRequiredService<RequestContextAccessor>();

        // 403 rather than 401. The credential is valid and was understood; what
        // is refused is everything else this caller wanted to do until they have
        // done the one thing they owe. A 401 would send a client back to
        // re-authenticate, which succeeds and changes nothing.
        ProblemDetails problem = ProblemDetailsFactory.Forbidden(
            RefusalCode,
            "This password must be changed before anything else can be done.",
            requestContext.CorrelationId,
            context.Request.Path.Value);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, ProblemJson));
    }

    /// <summary>
    /// What the caller acts on. A client seeing this knows to send the person to
    /// the change-password flow rather than to re-authenticate or to ask for a
    /// permission -- which is the whole reason a machine-readable code exists
    /// alongside the sentence.
    /// </summary>
    public const string RefusalCode = "IDENTITY.PASSWORD_CHANGE_REQUIRED";

    /// <summary>
    /// camelCase, matching every other response the Platform writes. Separate
    /// from the minimal-API serializer because this runs in middleware, before
    /// any endpoint's serialization applies.
    /// </summary>
    private static readonly JsonSerializerOptions ProblemJson = new(JsonSerializerDefaults.Web);

    private static bool Owes(ClaimsPrincipal? user)
        => user?.Identity?.IsAuthenticated == true
           && string.Equals(user.FindFirst(PlatformClaims.MustChangePassword)?.Value, "true", StringComparison.Ordinal);

    /// <summary>
    /// Whether this endpoint is one of the few reachable anyway.
    /// <para>
    /// An endpoint that allows anonymous access is included without needing the
    /// marker: it was reachable with no credential at all, so refusing it to
    /// somebody holding a valid one would be a strange kind of security.
    /// </para>
    /// </summary>
    private static bool IsPermitted(HttpContext context)
    {
        Endpoint? endpoint = context.GetEndpoint();

        if (endpoint is null)
        {
            return true;
        }

        return endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is not null
               || endpoint.Metadata.GetMetadata<AllowWhilePasswordChangePendingAttribute>() is not null;
    }
}
