using CCP.Kernel.Api.Context;
using CCP.Kernel.Api.Errors;
using CCP.Kernel.Api.Security;
using CCP.Kernel.Application.Observability;
using CCP.Kernel.Results;
using CCP.Modules.Identity.Application;
using CCP.Modules.Identity.Application.Abstractions;
using CCP.Modules.Identity.Application.Passkeys;
using CCP.Modules.Identity.Contracts.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Identity.Api;

/// <summary>Where a passkey ceremony begins, and what the device sends back.</summary>
public sealed record BeginPasskeyRegistrationRequest(string CurrentPassword);

/// <summary>What <c>navigator.credentials.create</c> produced, base64url throughout.</summary>
public sealed record CompletePasskeyRegistrationRequest(
    string Name,
    string ClientDataJson,
    string AttestationObject);

/// <summary>What <c>navigator.credentials.get</c> produced, base64url throughout.</summary>
public sealed record CompletePasskeySignInRequest(
    string CredentialId,
    string ClientDataJson,
    string AuthenticatorData,
    string Signature,
    string? UserHandle);

/// <summary>
/// Signing in with a fingerprint, and the passkeys that make it possible.
/// <para>
/// The two anonymous endpoints carry the same strict rate limit as password
/// sign-in. They are cheaper to serve than a password attempt — a signature
/// check rather than a deliberately slow hash — but the limit is not about
/// cost: it is about how fast somebody may try, and that answer does not change
/// with the credential.
/// </para>
/// <para>
/// All four return 404 when passkeys are switched off, rather than 403. A
/// feature that is not enabled here is a feature that is not here.
/// </para>
/// </summary>
public static class PasskeyEndpoints
{
    public static void MapPasskeyEndpoints(this IEndpointRouteBuilder versionGroup)
    {
        MapSignIn(versionGroup);
        MapManagement(versionGroup);
    }

    private static void MapSignIn(IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder group = versionGroup
            .MapGroup("/auth/passkey")
            .WithTags("Authentication");

        group.MapPost("/options", async (
            HttpContext context,
            [FromServices] BeginPasskeySignInHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            [FromServices] IOptions<IdentityOptions> options,
            CancellationToken cancellationToken) =>
        {
            if (!options.Value.WebAuthn.Enabled)
            {
                return Results.NotFound();
            }

            Result<PasskeySignInOptionsDto> result = await handler.HandleAsync(
                new BeginPasskeySignInCommand(requestContext.IpAddress), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .Produces<PasskeySignInOptionsDto>(StatusCodes.Status200OK)
            .WithName("BeginPasskeySignIn")
            .WithSummary("Issues a one-time challenge for signing in with a passkey.");

        group.MapPost(string.Empty, async (
            CompletePasskeySignInRequest request,
            HttpContext context,
            [FromServices] CompletePasskeySignInHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            [FromServices] PlatformMetrics metrics,
            [FromServices] IOptions<IdentityOptions> options,
            CancellationToken cancellationToken) =>
        {
            if (!options.Value.WebAuthn.Enabled)
            {
                return Results.NotFound();
            }

            Result<AuthenticationResultDto> result = await handler.HandleAsync(
                new CompletePasskeySignInCommand(
                    new PasskeyAssertionEvidence(
                        request.CredentialId,
                        request.ClientDataJson,
                        request.AuthenticatorData,
                        request.Signature,
                        request.UserHandle),
                    requestContext.IpAddress,
                    requestContext.UserAgent,
                    DeviceFingerprint: null),
                cancellationToken);

            // Counted beside password attempts rather than separately, so the
            // ratio an alert watches stays the ratio of sign-ins that work.
            metrics.AuthenticationAttempted(result.IsSuccess);

            return result.ToHttpResult(context, requestContext);
        })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .Produces<AuthenticationResultDto>(StatusCodes.Status200OK)
            .WithName("CompletePasskeySignIn")
            .WithSummary("Signs in with a passkey, and starts a session.");
    }

    private static void MapManagement(IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder group = versionGroup
            .MapGroup("/me/passkeys")
            .WithTags("Current user");

        group.MapGet(string.Empty, async (
            HttpContext context,
            [FromServices] PasskeyQueryHandlers handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            if (!AuthenticationEndpoints.TryGetIdentity(context.User, out Guid userId, out _))
            {
                return Results.Unauthorized();
            }

            Result<IReadOnlyList<PasskeyDto>> result =
                await handler.ListAsync(userId, cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new AuthenticatedUserOnlyAttribute(
                "Seeing which devices can sign in as you needs no permission, and somebody who "
                + "cannot see the list cannot notice a device they do not recognise."))
            .Produces<IReadOnlyList<PasskeyDto>>(StatusCodes.Status200OK)
            .WithName("GetMyPasskeys")
            .WithSummary("Lists the passkeys registered to the caller.");

        group.MapPost("/options", async (
            BeginPasskeyRegistrationRequest request,
            HttpContext context,
            [FromServices] BeginPasskeyRegistrationHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            [FromServices] IOptions<IdentityOptions> options,
            CancellationToken cancellationToken) =>
        {
            if (!options.Value.WebAuthn.Enabled)
            {
                return Results.NotFound();
            }

            if (!AuthenticationEndpoints.TryGetIdentity(context.User, out Guid userId, out _))
            {
                return Results.Unauthorized();
            }

            Result<PasskeyRegistrationOptionsDto> result = await handler.HandleAsync(
                new BeginPasskeyRegistrationCommand(userId, request.CurrentPassword),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new AuthenticatedUserOnlyAttribute(
                "Adding a way into your own account needs the password to that account, which "
                + "the handler checks, rather than a permission."))
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .Produces<PasskeyRegistrationOptionsDto>(StatusCodes.Status200OK)
            .WithName("BeginPasskeyRegistration")
            .WithSummary("Issues a one-time challenge for adding a passkey, after checking the password.");

        group.MapPost(string.Empty, async (
            CompletePasskeyRegistrationRequest request,
            HttpContext context,
            [FromServices] CompletePasskeyRegistrationHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            [FromServices] IOptions<IdentityOptions> options,
            CancellationToken cancellationToken) =>
        {
            if (!options.Value.WebAuthn.Enabled)
            {
                return Results.NotFound();
            }

            if (!AuthenticationEndpoints.TryGetIdentity(context.User, out Guid userId, out _))
            {
                return Results.Unauthorized();
            }

            Result<PasskeyDto> result = await handler.HandleAsync(
                new CompletePasskeyRegistrationCommand(
                    userId,
                    string.IsNullOrWhiteSpace(request.Name) ? "Passkey" : request.Name,
                    new PasskeyRegistrationEvidence(request.ClientDataJson, request.AttestationObject)),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new AuthenticatedUserOnlyAttribute(
                "The challenge issued a moment ago is what authorises this, and it was issued "
                + "only after the password was checked."))
            .Produces<PasskeyDto>(StatusCodes.Status200OK)
            .WithName("CompletePasskeyRegistration")
            .WithSummary("Stores a passkey the caller's device has just created.");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext context,
            [FromServices] PasskeyQueryHandlers handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            if (!AuthenticationEndpoints.TryGetIdentity(context.User, out Guid userId, out _))
            {
                return Results.Unauthorized();
            }

            Result result = await handler.RemoveAsync(userId, id, cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new AuthenticatedUserOnlyAttribute(
                "Removing a device from your own account is something you must always be able "
                + "to do, and quickly: it is what somebody does the moment a laptop is lost."))
            .Produces(StatusCodes.Status204NoContent)
            .WithName("RemoveMyPasskey")
            .WithSummary("Removes one of the caller's passkeys.");
    }
}
