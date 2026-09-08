using CCP.Kernel.Api.Context;
using CCP.Kernel.Api.Errors;
using CCP.Kernel.Api.Security;
using CCP.Kernel.Paging;
using CCP.Kernel.Results;
using CCP.Modules.Identity.Application.Users;
using CCP.Modules.Identity.Contracts.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CCP.Modules.Identity.Api;

/// <summary>
/// User administration and the caller's own profile.
/// <para>
/// Every administrative endpoint declares a <c>platform.users.*</c> permission.
/// Those permissions are strings today — the handler that evaluates them arrives
/// with the Authorization module in Phase 4. Until then these endpoints require
/// authentication but the permission is <b>not yet enforced</b>, which is
/// recorded here and in DEVELOPMENT_STATUS rather than left to be discovered.
/// </para>
/// </summary>
public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder versionGroup)
    {
        MapCurrentUserEndpoints(versionGroup);
        MapAdministrationEndpoints(versionGroup);
    }

    private static void MapCurrentUserEndpoints(IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder me = versionGroup.MapGroup("/me").WithTags("Current user");

        me.MapGet("/", async (
            HttpContext context,
            [FromServices] GetUserHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            if (!AuthenticationEndpoints.TryGetIdentity(context.User, out Guid userId, out _))
            {
                return Results.Unauthorized();
            }

            Result<UserDto> result = await handler.HandleAsync(new GetUserQuery(userId), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new AuthenticatedUserOnlyAttribute("Reading one's own profile needs no permission."))
            .WithName("GetCurrentUser")
            .WithSummary("Returns the signed-in user's profile.");

        me.MapGet("/sessions", async (
            HttpContext context,
            [FromServices] GetMySessionsHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            if (!AuthenticationEndpoints.TryGetIdentity(context.User, out Guid userId, out Guid sessionId))
            {
                return Results.Unauthorized();
            }

            Result<IReadOnlyList<SessionDto>> result =
                await handler.HandleAsync(new GetMySessionsQuery(userId, sessionId), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new AuthenticatedUserOnlyAttribute("Seeing one's own sessions needs no permission."))
            .WithName("GetMySessions")
            .WithSummary("Lists the caller's active sessions.");

        me.MapGet("/login-history", async (
            int? count,
            HttpContext context,
            [FromServices] GetMyLoginHistoryHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            if (!AuthenticationEndpoints.TryGetIdentity(context.User, out Guid userId, out _))
            {
                return Results.Unauthorized();
            }

            Result<IReadOnlyList<LoginAttemptDto>> result = await handler.HandleAsync(
                new GetMyLoginHistoryQuery(userId, count ?? 20), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new AuthenticatedUserOnlyAttribute(
                "A user seeing failed attempts on their own account is the earliest signal that "
                + "someone is trying it."))
            .WithName("GetMyLoginHistory")
            .WithSummary("Lists the caller's recent sign-in attempts, successful and failed.");
    }

    private static void MapAdministrationEndpoints(IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder users = versionGroup.MapGroup("/users").WithTags("Users");

        users.MapGet("/", async (
            int? page,
            int? pageSize,
            string? sort,
            string? q,
            string? status,
            HttpContext context,
            [FromServices] SearchUsersHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<PageRequest> pageRequest = PageRequest.Create(page, pageSize, sort);

            if (pageRequest.IsFailure)
            {
                return pageRequest.ToHttpResult(context, requestContext);
            }

            Result<PagedResult<UserDto>> result = await handler.HandleAsync(
                new SearchUsersQuery(pageRequest.Value, q, status), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.users.view"))
            .WithName("SearchUsers")
            .WithSummary("Lists users, filtered and paged.");

        users.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext context,
            [FromServices] GetUserHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<UserDto> result = await handler.HandleAsync(new GetUserQuery(id), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.users.view"))
            .WithName("GetUser")
            .WithSummary("Returns one user.");

        users.MapPost("/", async (
            CreateUserRequest request,
            HttpContext context,
            [FromServices] CreateUserHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result validation = request.Validate();

            if (validation.IsFailure)
            {
                return validation.ToHttpResult(context, requestContext);
            }

            Result<UserDto> result = await handler.HandleAsync(
                new CreateUserCommand(
                    request.Username, request.Email, request.DisplayName, request.InitialPassword),
                cancellationToken);

            return result.IsSuccess
                ? result.ToCreatedResult($"/api/v1/users/{result.Value.Id}", context, requestContext)
                : result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.users.create"))
            // A new account is a new way in, and one an intruder controls the
            // password of.
            .WithMetadata(new RequireStepUpAttribute())
            .WithName("CreateUser")
            .WithSummary("Creates a user who must change their password at first sign-in.");

        users.MapPut("/{id:guid}", async (
            Guid id,
            UpdateUserRequest request,
            HttpContext context,
            [FromServices] UpdateUserHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result validation = request.Validate();

            if (validation.IsFailure)
            {
                return validation.ToHttpResult(context, requestContext);
            }

            Result<UserDto> result = await handler.HandleAsync(
                new UpdateUserCommand(id, request.Email, request.DisplayName), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.users.edit"))
            .WithName("UpdateUser")
            .WithSummary("Updates a user's email and display name.");

        MapStatusAction(users, "enable", UserStatusAction.Enable, "platform.users.edit");
        MapStatusAction(users, "disable", UserStatusAction.Disable, "platform.users.edit");
        MapStatusAction(users, "unlock", UserStatusAction.Unlock, "platform.users.edit");
    }

    private static void MapStatusAction(
        RouteGroupBuilder users,
        string segment,
        UserStatusAction action,
        string permission)
        => users.MapPost($"/{{id:guid}}/{segment}", async (
            Guid id,
            HttpContext context,
            [FromServices] ChangeUserStatusHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            // The acting user comes from the token, so the "cannot act on
            // yourself" rule cannot be bypassed by lying in the request body.
            if (!AuthenticationEndpoints.TryGetIdentity(context.User, out Guid actingUserId, out _))
            {
                return Results.Unauthorized();
            }

            Result result = await handler.HandleAsync(
                new ChangeUserStatusCommand(id, actingUserId, action), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute(permission))
            // Enabling a disabled account and unlocking a locked-out one both
            // restore a way in that something had deliberately closed, and
            // disabling is how an intruder removes the person who would notice.
            .WithMetadata(new RequireStepUpAttribute())
            .WithName($"{action}User")
            .WithSummary($"{action}s a user account.");
}

/// <summary>Create-user request body.</summary>
public sealed record CreateUserRequest(
    string Username,
    string Email,
    string DisplayName,
    string InitialPassword)
{
    public Result Validate()
    {
        List<Error> errors = [];

        if (string.IsNullOrWhiteSpace(Username))
        {
            errors.Add(Error.Validation("IDENTITY.USERNAME_REQUIRED", "A username is required.", "username"));
        }

        if (string.IsNullOrWhiteSpace(Email))
        {
            errors.Add(Error.Validation("IDENTITY.EMAIL_REQUIRED", "An email address is required.", "email"));
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            errors.Add(Error.Validation(
                "IDENTITY.DISPLAY_NAME_REQUIRED", "A display name is required.", "displayName"));
        }

        if (string.IsNullOrEmpty(InitialPassword))
        {
            errors.Add(Error.Validation(
                "IDENTITY.PASSWORD_REQUIRED", "An initial password is required.", "initialPassword"));
        }
        else if (InitialPassword.Length > 256)
        {
            errors.Add(Error.Validation(
                "IDENTITY.PASSWORD_TOO_LONG", "The initial password is too long.", "initialPassword"));
        }

        return errors.Count == 0 ? Result.Success() : Result.Failure(errors);
    }
}

/// <summary>Update-user request body.</summary>
public sealed record UpdateUserRequest(string Email, string DisplayName)
{
    public Result Validate()
    {
        List<Error> errors = [];

        if (string.IsNullOrWhiteSpace(Email))
        {
            errors.Add(Error.Validation("IDENTITY.EMAIL_REQUIRED", "An email address is required.", "email"));
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            errors.Add(Error.Validation(
                "IDENTITY.DISPLAY_NAME_REQUIRED", "A display name is required.", "displayName"));
        }

        return errors.Count == 0 ? Result.Success() : Result.Failure(errors);
    }
}
