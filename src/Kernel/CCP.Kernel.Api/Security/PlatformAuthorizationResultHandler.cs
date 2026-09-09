using System.Text.Json;
using CCP.Kernel.Api.Context;
using CCP.Kernel.Api.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Kernel.Api.Security;

/// <summary>
/// Gives a refused request a body that says which refusal it was.
/// <para>
/// The framework's own handler answers a bare 403 with nothing in it. That is
/// fine for a prober and useless for the person actually using the Platform,
/// because two very different situations arrive looking identical: they lack a
/// permission, or they hold it and their elevation has lapsed. The remedies are
/// opposite — ask an administrator, or confirm a second factor — and a client
/// that cannot tell them apart sends people to ask for access they already have.
/// </para>
/// <para>
/// The status code stays 403 in both cases, deliberately. Nothing here is a
/// hint to someone who is not signed in: an unauthenticated caller never reaches
/// this handler, because a missing identity produces 401 instead.
/// </para>
/// <para>
/// The shape is the same RFC 9457 body as every other failure (ADR-008), so a
/// client keeps one error path rather than two.
/// </para>
/// </summary>
public sealed class PlatformAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    /// <summary>The caller holds the permission but must confirm a second factor.</summary>
    public const string StepUpRequiredCode = "SECURITY.STEP_UP_REQUIRED";

    /// <summary>The caller does not hold what this endpoint requires.</summary>
    public const string ForbiddenCode = "PLATFORM.FORBIDDEN";

    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        // Only the forbidden case is ours. A challenge is the authentication
        // scheme's business, and rewriting it here would break the 401 the
        // frontend relies on to know a session has lapsed.
        if (!authorizeResult.Forbidden)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);

            return;
        }

        bool stepUp = authorizeResult.AuthorizationFailure?.FailureReasons
            .Any(reason => reason.Message == StepUpRequirement.FailureReason) == true;

        // Populated by the request-context middleware, which runs before
        // authorization. It is the only diagnostic the caller gets, and the
        // thread that ties this refusal to the log line explaining it.
        var requestContext = context.RequestServices
            .GetRequiredService<RequestContextAccessor>();

        ProblemDetails problem = ProblemDetailsFactory.Forbidden(
            stepUp ? StepUpRequiredCode : ForbiddenCode,
            stepUp
                ? "This action needs your identity confirmed again. Verify your second factor and retry."
                : "You do not hold the permission this action requires.",
            requestContext.CorrelationId,
            context.Request.Path.Value);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(problem, ProblemJson));
    }

    /// <summary>
    /// camelCase, matching every other response the Platform writes. The
    /// serializer here is separate from the one minimal APIs use because this
    /// runs inside middleware, before any endpoint's serialization applies.
    /// </summary>
    private static readonly JsonSerializerOptions ProblemJson = new(JsonSerializerDefaults.Web);
}
