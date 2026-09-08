using System.Security.Claims;
using CCP.Kernel.Api.Context;
using CCP.Kernel.Api.Errors;
using CCP.Kernel.Api.Security;
using CCP.Kernel.Results;
using CCP.Modules.Identity.Application.Authentication;
using CCP.Modules.Identity.Contracts.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace CCP.Modules.Identity.Api;

/// <summary>
/// Sign-in, refresh and sign-out.
/// <para>
/// Sign-in and refresh are anonymous and carry the strict authentication rate
/// limit. The refresh token appears in request and response bodies because the
/// caller is the BFF, running server-side; the browser never sees it
/// (ADR-006 §13.2).
/// </para>
/// </summary>
public static class AuthenticationEndpoints
{
    public static void MapAuthenticationEndpoints(this IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder group = versionGroup
            .MapGroup("/auth")
            .WithTags("Authentication");

        group.MapPost("/login", async (
            LoginRequest request,
            HttpContext context,
            [FromServices] SignInHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result validation = request.Validate();

            if (validation.IsFailure)
            {
                return validation.ToHttpResult(context, requestContext);
            }

            Result<AuthenticationResultDto> result = await handler.HandleAsync(
                new SignInCommand(
                    request.Username,
                    request.Password,
                    requestContext.IpAddress,
                    requestContext.UserAgent,
                    request.DeviceFingerprint),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            // Declares what a success returns, so the OpenAPI document describes
            // the response and not merely the request. The frontend generates its
            // types from that document; without this the response shape is a guess
            // written by hand, which is how `expiresIn` and a top-level
            // `mustChangePassword` reached production.
            .Produces<AuthenticationResultDto>(StatusCodes.Status200OK)
            .WithName("SignIn")
            .WithSummary("Authenticates a user and starts a session.");

        group.MapPost("/refresh", async (
            RefreshRequest request,
            HttpContext context,
            [FromServices] RefreshTokenHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result validation = request.Validate();

            if (validation.IsFailure)
            {
                return validation.ToHttpResult(context, requestContext);
            }

            Result<AuthenticationResultDto> result = await handler.HandleAsync(
                new RefreshCommand(request.RefreshToken, requestContext.IpAddress, requestContext.UserAgent),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            // Declares what a success returns, so the OpenAPI document describes
            // the response and not merely the request.
            .Produces<AuthenticationResultDto>(StatusCodes.Status200OK)
            .WithName("RefreshToken")
            .WithSummary("Exchanges a refresh token for a new token pair.");

        group.MapPost("/logout", async (
            HttpContext context,
            [FromServices] SignOutHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            // Identity comes from the validated token, never from the request
            // body. Taking a user id from the caller would let anyone end
            // anyone else's session.
            if (!TryGetIdentity(context.User, out Guid userId, out Guid sessionId))
            {
                return Results.Unauthorized();
            }

            Result result = await handler.HandleAsync(
                new SignOutCommand(sessionId, userId), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new AuthenticatedUserOnlyAttribute(
                "Any signed-in user may end their own session; a permission granted to everyone "
                + "would make the authorization model less readable, not more secure."))
            .WithName("SignOut")
            .WithSummary("Ends the current session, revoking its tokens server-side.");
    }

    /// <summary>Reads the user and session ids from the authenticated principal.</summary>
    internal static bool TryGetIdentity(ClaimsPrincipal principal, out Guid userId, out Guid sessionId)
    {
        userId = Guid.Empty;
        sessionId = Guid.Empty;

        string? subject = principal.FindFirst("sub")?.Value
                       ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        string? session = principal.FindFirst("sid")?.Value;

        return Guid.TryParse(subject, out userId) && Guid.TryParse(session, out sessionId);
    }
}

/// <summary>Sign-in request body.</summary>
/// <param name="Username">The account name.</param>
/// <param name="Password">The password. Never logged, never echoed.</param>
/// <param name="DeviceFingerprint">
/// Optional coarse device identifier, so a user can tell their sessions apart.
/// Client-supplied and therefore not a security control.
/// </param>
public sealed record LoginRequest(string Username, string Password, string? DeviceFingerprint)
{
    /// <summary>
    /// Checks the request is well-formed.
    /// <para>
    /// JSON deserialization can leave a non-nullable string null, so the record's
    /// type signature is not a guarantee. Without this, a malformed body reaches
    /// the handler and surfaces as a 500 — which is both the wrong status for a
    /// client error and noise in error monitoring.
    /// </para>
    /// <para>
    /// Length bounds are here too: an unbounded password is a cheap way to make
    /// the server perform expensive Argon2id work, before any account even
    /// exists to check it against.
    /// </para>
    /// </summary>
    public Result Validate()
    {
        List<Error> errors = [];

        if (string.IsNullOrWhiteSpace(Username))
        {
            errors.Add(Error.Validation(
                "IDENTITY.USERNAME_REQUIRED", "A username is required.", "username"));
        }
        else if (Username.Length > 64)
        {
            errors.Add(Error.Validation(
                "IDENTITY.USERNAME_LENGTH", "The username is too long.", "username"));
        }

        if (string.IsNullOrEmpty(Password))
        {
            errors.Add(Error.Validation(
                "IDENTITY.PASSWORD_REQUIRED", "A password is required.", "password"));
        }
        else if (Password.Length > 256)
        {
            errors.Add(Error.Validation(
                "IDENTITY.PASSWORD_TOO_LONG", "The password is too long.", "password"));
        }

        return errors.Count == 0 ? Result.Success() : Result.Failure(errors);
    }
}

/// <summary>Refresh request body.</summary>
public sealed record RefreshRequest(string RefreshToken)
{
    public Result Validate()
        => string.IsNullOrWhiteSpace(RefreshToken)
            ? Result.Failure(Error.Validation(
                "IDENTITY.REFRESH_TOKEN_REQUIRED", "A refresh token is required.", "refreshToken"))
            : Result.Success();
}
