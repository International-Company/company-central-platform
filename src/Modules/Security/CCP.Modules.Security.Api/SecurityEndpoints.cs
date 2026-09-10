using System.Security.Claims;
using CCP.Kernel.Api.Context;
using CCP.Kernel.Api.Errors;
using CCP.Kernel.Api.Security;
using CCP.Kernel.Paging;
using CCP.Kernel.Results;
using CCP.Modules.Security.Application.Abstractions;
using CCP.Modules.Security.Application.Mfa;
using CCP.Modules.Security.Contracts.Dtos;
using CCP.Modules.Security.Domain.Events;
using CCP.Modules.Security.Domain.Mfa;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace CCP.Modules.Security.Api;

/// <summary>
/// Two-factor authentication and the security event log.
/// <para>
/// Every MFA endpoint carries the strict authentication rate limit. They accept
/// codes, so they are guessing surfaces — a six-digit code with a browsing-sized
/// budget would be guessable in an afternoon.
/// </para>
/// </summary>
public static class SecurityEndpoints
{
    public static void MapSecurityEndpoints(this IEndpointRouteBuilder versionGroup)
    {
        MapMfaEndpoints(versionGroup);
        MapSecurityEventEndpoints(versionGroup);
    }

    private static void MapMfaEndpoints(IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder mfa = versionGroup.MapGroup("/me/mfa").WithTags("Two-factor authentication");

        mfa.MapGet("/", async (
            HttpContext context,
            [FromServices] ISecurityRepository repository,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetIdentity(context, out Guid userId, out _))
            {
                return Results.Unauthorized();
            }

            MfaEnrolment? enrolment = await repository.FindEnrolmentAsync(userId, cancellationToken);

            // Carries no secret and no code — only whether a factor exists and
            // how healthy it is.
            return Results.Ok(new MfaStatusDto(
                IsEnrolled: enrolment is not null,
                IsActive: enrolment?.IsActive ?? false,
                ActivatedAt: enrolment?.ActivatedAt,
                LastUsedAt: enrolment?.LastUsedAt,
                RemainingRecoveryCodes: enrolment?.RemainingRecoveryCodes ?? 0));
        })
            .RequireAuthorization()
            .WithMetadata(new AuthenticatedUserOnlyAttribute(
                "Seeing one's own two-factor status needs no permission."))
            .RequireRateLimiting(RateLimitPolicies.Read)
            .Produces<MfaStatusDto>(StatusCodes.Status200OK)
            .WithName("GetMfaStatus")
            .WithSummary("Returns whether the caller has two-factor authentication enabled.");

        mfa.MapPost("/enrol", async (
            HttpContext context,
            [FromServices] BeginMfaEnrolmentHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetIdentity(context, out Guid userId, out string username))
            {
                return Results.Unauthorized();
            }

            Result<MfaEnrolmentDto> result = await handler.HandleAsync(
                new BeginMfaEnrolmentCommand(userId, username), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new AuthenticatedUserOnlyAttribute(
                "Protecting one's own account needs no permission — requiring one would let an "
                + "administrator prevent people securing their accounts."))
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .Produces<MfaEnrolmentDto>(StatusCodes.Status200OK)
            .WithName("BeginMfaEnrolment")
            .WithSummary("Issues a TOTP secret. Returns the QR data once and never again.");

        mfa.MapPost("/confirm", async (
            MfaCodeRequest request,
            HttpContext context,
            [FromServices] ConfirmMfaEnrolmentHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result validation = request.Validate();

            if (validation.IsFailure)
            {
                return validation.ToHttpResult(context, requestContext);
            }

            if (!TryGetIdentity(context, out Guid userId, out string username))
            {
                return Results.Unauthorized();
            }

            Result<RecoveryCodesDto> result = await handler.HandleAsync(
                new ConfirmMfaEnrolmentCommand(userId, username, request.Code), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new AuthenticatedUserOnlyAttribute("Confirming one's own enrolment."))
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .Produces<RecoveryCodesDto>(StatusCodes.Status200OK)
            .WithName("ConfirmMfaEnrolment")
            .WithSummary("Activates enrolment and returns recovery codes, shown once.");

        mfa.MapPost("/verify", async (
            MfaVerifyRequest request,
            HttpContext context,
            [FromServices] VerifyMfaHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result validation = request.Validate();

            if (validation.IsFailure)
            {
                return validation.ToHttpResult(context, requestContext);
            }

            if (!TryGetIdentity(context, out Guid userId, out string username))
            {
                return Results.Unauthorized();
            }

            // The session id is required, not optional: an elevation that is not
            // bound to a session would privilege every device the user holds.
            if (!TryGetSessionId(context, out Guid sessionId))
            {
                return Results.Unauthorized();
            }

            Result<MfaVerificationDto> result = await handler.HandleAsync(
                new VerifyMfaCommand(
                    userId, username, sessionId, request.Code, request.IsRecoveryCode ?? false,
                    requestContext.IpAddress),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new AuthenticatedUserOnlyAttribute(
                "Proving one's own second factor, for sign-in or step-up."))
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .Produces<MfaVerificationDto>(StatusCodes.Status200OK)
            .WithName("VerifyMfa")
            .WithSummary("Verifies a TOTP or recovery code.");

        mfa.MapPost("/disable", async (
            MfaCodeRequest request,
            HttpContext context,
            [FromServices] DisableMfaHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result validation = request.Validate();

            if (validation.IsFailure)
            {
                return validation.ToHttpResult(context, requestContext);
            }

            if (!TryGetIdentity(context, out Guid userId, out string username))
            {
                return Results.Unauthorized();
            }

            Result result = await handler.HandleAsync(
                new DisableMfaCommand(userId, username, request.Code), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new AuthenticatedUserOnlyAttribute(
                "Removing one's own factor, which requires proving it first."))
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .WithName("DisableMfa")
            .WithSummary("Turns off two-factor authentication, after proving the current factor.");

        versionGroup.MapPost("/security/users/{userId:guid}/mfa/reset", async (
            Guid userId,
            ResetMfaRequest request,
            HttpContext context,
            [FromServices] ResetMfaHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetIdentity(context, out Guid actorUserId, out string actorUsername))
            {
                return Results.Unauthorized();
            }

            Result result = await handler.HandleAsync(
                new ResetMfaCommand(userId, actorUserId, actorUsername, request.Reason),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.security.manage"))

            // Step-up as well as the permission, and this is the one endpoint
            // where that matters most: it strips a second factor from an
            // account, which is the first thing an attacker does after taking
            // one. Demanding recent proof of the administrator's own factor
            // means a stolen session cannot be used to disarm everybody else --
            // and it means the person removing a factor has one.
            .WithMetadata(new RequireStepUpAttribute())
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .WithTags("Security")
            .WithName("ResetMfa")
            .WithSummary("Clears somebody else's second factor when they have lost it.");
    }

    private static void MapSecurityEventEndpoints(IEndpointRouteBuilder versionGroup) =>
        versionGroup.MapGet("/security/events", async (
            int? page,
            int? pageSize,
            Guid? userId,
            string? eventType,
            string? minimumSeverity,
            DateTimeOffset? from,
            DateTimeOffset? to,
            HttpContext context,
            [FromServices] ISecurityRepository repository,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<PageRequest> pageRequest = PageRequest.Create(page, pageSize, null);

            if (pageRequest.IsFailure)
            {
                return pageRequest.ToHttpResult(context, requestContext);
            }

            SecuritySeverity? severity = null;

            if (!string.IsNullOrWhiteSpace(minimumSeverity))
            {
                if (!Enum.TryParse(minimumSeverity, ignoreCase: true, out SecuritySeverity parsed))
                {
                    return Result.Failure(Error.Validation(
                        "SECURITY.INVALID_SEVERITY",
                        $"Unknown severity. Allowed: {string.Join(", ", Enum.GetNames<SecuritySeverity>())}.",
                        "minimumSeverity"))
                        .ToHttpResult(context, requestContext);
                }

                severity = parsed;
            }

            // The window defaults to the last seven days rather than to
            // everything. An unbounded query over a table that grows with every
            // failed sign-in is a scan, and someone opening a dashboard should
            // not be able to cause one.
            DateTimeOffset resolvedTo = to ?? DateTimeOffset.UtcNow;
            DateTimeOffset resolvedFrom = from ?? resolvedTo.AddDays(-7);

            (IReadOnlyList<SecurityEvent> items, long total) =
                await repository.SearchSecurityEventsAsync(
                    userId, eventType, severity, resolvedFrom, resolvedTo,
                    pageRequest.Value.Skip, pageRequest.Value.PageSize, cancellationToken);

            return Results.Ok(new PagedResult<SecurityEventDto>(
                [.. items.Select(e => new SecurityEventDto(
                    e.Id, e.EventType, e.Severity.ToString(), e.UserId, e.Username,
                    e.IpAddress, e.Details, e.CorrelationId, e.OccurredAt))],
                pageRequest.Value.Page,
                pageRequest.Value.PageSize,
                total));
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.security.view"))
            .RequireRateLimiting(RateLimitPolicies.Read)
            .WithTags("Security")
            .Produces<PagedResult<SecurityEventDto>>(StatusCodes.Status200OK)
            .WithName("SearchSecurityEvents")
            .WithSummary("Searches the security event log within a bounded time window.");

    private static bool TryGetSessionId(HttpContext context, out Guid sessionId)
        => Guid.TryParse(context.User.FindFirst("sid")?.Value, out sessionId);

    private static bool TryGetIdentity(HttpContext context, out Guid userId, out string username)
    {
        username = context.User.FindFirst("username")?.Value ?? string.Empty;

        string? subject = context.User.FindFirst("sub")?.Value
                       ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out userId);
    }
}

/// <summary>A request carrying a single code.</summary>
public sealed record MfaCodeRequest(string Code)
{
    public Result Validate()
        => string.IsNullOrWhiteSpace(Code)
            ? Result.Failure(Error.Validation("SECURITY.CODE_REQUIRED", "A code is required.", "code"))
            : Code.Length > 32
                ? Result.Failure(Error.Validation(
                    "SECURITY.CODE_TOO_LONG", "That code is not a valid length.", "code"))
                : Result.Success();
}

/// <summary>A verification request, which may carry a TOTP or a recovery code.</summary>
public sealed record MfaVerifyRequest(string Code, bool? IsRecoveryCode)
{
    public Result Validate()
        => string.IsNullOrWhiteSpace(Code)
            ? Result.Failure(Error.Validation("SECURITY.CODE_REQUIRED", "A code is required.", "code"))
            : Code.Length > 32
                ? Result.Failure(Error.Validation(
                    "SECURITY.CODE_TOO_LONG", "That code is not a valid length.", "code"))
                : Result.Success();
}

/// <summary>
/// Clearing somebody else's second factor.
/// </summary>
/// <param name="Reason">
/// Why. Required, because "they lost their phone and I verified them in person"
/// and "somebody rang up claiming to be them" are the same operation and
/// opposite acts, and this sentence is the only thing that tells them apart.
/// </param>
public sealed record ResetMfaRequest(string Reason);
