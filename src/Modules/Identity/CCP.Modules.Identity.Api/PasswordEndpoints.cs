using CCP.Kernel.Api.Context;
using CCP.Kernel.Api.Errors;
using CCP.Kernel.Api.Security;
using CCP.Kernel.Results;
using CCP.Modules.Identity.Application.Passwords;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace CCP.Modules.Identity.Api;

/// <summary>
/// Password change and reset.
/// <para>
/// The two reset endpoints are anonymous, because someone who has forgotten
/// their password by definition cannot authenticate. That makes them the most
/// exposed surface in the module, which is why both are enumeration-safe and
/// both carry the strict authentication rate limit.
/// </para>
/// <para>
/// Password change is throttled with the same policy despite requiring
/// authentication: it verifies the current password, so it is a password-guessing
/// surface for anyone holding a stolen token.
/// </para>
/// </summary>
public static class PasswordEndpoints
{
    public static void MapPasswordEndpoints(this IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder group = versionGroup
            .MapGroup("/auth")
            .WithTags("Passwords");

        group.MapPost("/password/change", async (
            ChangePasswordRequest request,
            HttpContext context,
            [FromServices] ChangePasswordHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result validation = request.Validate();

            if (validation.IsFailure)
            {
                return validation.ToHttpResult(context, requestContext);
            }

            if (!AuthenticationEndpoints.TryGetIdentity(context.User, out Guid userId, out Guid sessionId))
            {
                return Results.Unauthorized();
            }

            Result result = await handler.HandleAsync(
                new ChangePasswordCommand(userId, sessionId, request.CurrentPassword, request.NewPassword),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new AuthenticatedUserOnlyAttribute(
                "Changing one's own password requires proving the current one, not a permission."))
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .WithMetadata(new AllowWhilePasswordChangePendingAttribute(
                "It is the obligation itself. Refusing this one would leave somebody "
                + "holding a temporary password with no way to stop holding it."))
            .WithName("ChangePassword")
            .WithSummary("Changes the caller's password and ends their other sessions.");

        group.MapPost("/password/forgot", async (
            ForgotPasswordRequest request,
            HttpContext context,
            [FromServices] RequestPasswordResetHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            // Deliberately no validation branch that could distinguish a
            // well-formed unknown address from a known one. Even a malformed
            // address returns the same 202.
            await handler.HandleAsync(
                new RequestPasswordResetCommand(request.Email ?? string.Empty, requestContext.IpAddress),
                cancellationToken);

            // 202 Accepted, always. "If that address belongs to an account, a
            // link has been sent" is the only thing an anonymous caller learns.
            return Results.Accepted();
        })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .WithName("ForgotPassword")
            .WithSummary("Requests a password reset link. Always succeeds, revealing nothing.");

        group.MapPost("/password/reset", async (
            ResetPasswordRequest request,
            HttpContext context,
            [FromServices] ResetPasswordHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result validation = request.Validate();

            if (validation.IsFailure)
            {
                return validation.ToHttpResult(context, requestContext);
            }

            Result result = await handler.HandleAsync(
                new ResetPasswordCommand(request.Token, request.NewPassword, requestContext.IpAddress),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .WithName("ResetPassword")
            .WithSummary("Redeems a reset token and sets a new password.");
    }
}

/// <summary>Password change request body.</summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword)
{
    public Result Validate()
    {
        List<Error> errors = [];

        if (string.IsNullOrEmpty(CurrentPassword))
        {
            errors.Add(Error.Validation(
                "IDENTITY.CURRENT_PASSWORD_REQUIRED", "The current password is required.", "currentPassword"));
        }

        if (string.IsNullOrEmpty(NewPassword))
        {
            errors.Add(Error.Validation(
                "IDENTITY.PASSWORD_REQUIRED", "A new password is required.", "newPassword"));
        }
        else if (NewPassword.Length > 256)
        {
            // Bounded before hashing: an unbounded password is a cheap way to
            // make the server do expensive Argon2id work.
            errors.Add(Error.Validation(
                "IDENTITY.PASSWORD_TOO_LONG", "The new password is too long.", "newPassword"));
        }

        return errors.Count == 0 ? Result.Success() : Result.Failure(errors);
    }
}

/// <summary>
/// Forgot-password request body. The email is nullable and unvalidated by
/// design — any rejection here would be an observable difference an attacker
/// could use.
/// </summary>
public sealed record ForgotPasswordRequest(string? Email);

/// <summary>Reset redemption request body.</summary>
public sealed record ResetPasswordRequest(string Token, string NewPassword)
{
    public Result Validate()
    {
        List<Error> errors = [];

        if (string.IsNullOrWhiteSpace(Token))
        {
            errors.Add(Error.Validation(
                "IDENTITY.RESET_TOKEN_REQUIRED", "A reset token is required.", "token"));
        }

        if (string.IsNullOrEmpty(NewPassword))
        {
            errors.Add(Error.Validation(
                "IDENTITY.PASSWORD_REQUIRED", "A new password is required.", "newPassword"));
        }
        else if (NewPassword.Length > 256)
        {
            errors.Add(Error.Validation(
                "IDENTITY.PASSWORD_TOO_LONG", "The new password is too long.", "newPassword"));
        }

        return errors.Count == 0 ? Result.Success() : Result.Failure(errors);
    }
}
