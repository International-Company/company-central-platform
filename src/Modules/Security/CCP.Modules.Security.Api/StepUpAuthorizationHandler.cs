using System.Security.Claims;
using CCP.Kernel.Api.Security;
using CCP.Kernel.Primitives;
using CCP.Modules.Security.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace CCP.Modules.Security.Api;

/// <summary>
/// Enforces <see cref="RequireStepUpAttribute"/>.
/// <para>
/// The requirement type lives in the kernel and the policy is built by the
/// Authorization module's provider, but the state it consults belongs to
/// Security. Splitting it this way is what lets an endpoint in Identity demand
/// step-up without Identity referencing Security (§6.2).
/// </para>
/// <para>
/// Failure here is a plain 403. It deliberately does not distinguish "you have
/// no second factor" from "your elevation lapsed" in the status code — the
/// client learns which from the error body, and an unauthenticated prober learns
/// nothing at all.
/// </para>
/// </summary>
public sealed class StepUpAuthorizationHandler(
    ISecurityRepository repository,
    IClock clock,
    ILogger<StepUpAuthorizationHandler> logger)
    : AuthorizationHandler<StepUpRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        StepUpRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.User.Identity?.IsAuthenticated != true)
        {
            // Unhandled rather than failed, so the framework answers 401 rather
            // than 403 — the same distinction the permission handler makes.
            return;
        }

        if (!TryGetUserId(context.User, out Guid userId)
            || !Guid.TryParse(context.User.FindFirst("sid")?.Value, out Guid sessionId))
        {
            logger.LogWarning(
                "An authenticated principal carried no usable subject or session claim. Denying step-up.");

            context.Fail();

            return;
        }

        if (await repository.HasValidStepUpAsync(userId, sessionId, clock.UtcNow))
        {
            context.Succeed(requirement);
        }
        else
        {
            // No logging of the denial itself: a user working through a run of
            // administrative screens after their elevation lapsed would generate
            // a burst of warnings that mean nothing. The security event log
            // records the confirmations, which is the signal worth keeping.
            //
            // Carrying a reason, so the response can say "confirm your identity"
            // rather than "you do not have permission". The status stays 403
            // either way.
            context.Fail(new AuthorizationFailureReason(this, StepUpRequirement.FailureReason));
        }
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        string? subject = principal.FindFirst("sub")?.Value
                       ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out userId);
    }
}
