using CCP.Kernel.Api.Context;
using CCP.Kernel.Api.Errors;
using CCP.Kernel.Api.Security;
using CCP.Kernel.Results;
using CCP.Modules.Authorization.Application.Abstractions;
using CCP.Modules.Authorization.Application.Grants;
using CCP.Modules.Authorization.Contracts.Dtos;
using CCP.Modules.Authorization.Domain.Scopes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CCP.Modules.Authorization.Api;

/// <summary>
/// Roles, permissions, grants, and the check endpoint business applications use.
/// </summary>
public static class AuthorizationEndpoints
{
    public static void MapAuthorizationEndpoints(this IEndpointRouteBuilder versionGroup)
    {
        MapMyPermissions(versionGroup);
        MapCheckEndpoint(versionGroup);
        MapRoleEndpoints(versionGroup);
        MapGrantEndpoints(versionGroup);
    }

    private static void MapMyPermissions(IEndpointRouteBuilder versionGroup) =>
        versionGroup.MapGet("/me/permissions", async (
            HttpContext context,
            [FromServices] IPermissionResolver resolver,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(context, out Guid userId))
            {
                return Results.Unauthorized();
            }

            EffectivePermissions permissions =
                await resolver.GetEffectivePermissionsAsync(userId, cancellationToken);

            string? unitPath = await resolver.GetUserUnitPathAsync(userId, cancellationToken);

            // The frontend uses this to hide controls the user cannot use. That
            // is UX, not security — the backend enforces independently, and a
            // hidden button is not a protected operation (P6).
            var result = new MyPermissionsDto(
                [.. permissions.PermissionNames.Order(StringComparer.Ordinal)],
                unitPath is not null);

            return Results.Ok(result);
        })
            .RequireAuthorization()
            .WithMetadata(new AuthenticatedUserOnlyAttribute(
                "Reading one's own permissions needs no permission; it would be circular."))
            .WithTags("Authorization")
            .WithName("GetMyPermissions")
            .WithSummary("Lists the caller's permissions, for hiding controls they cannot use.");

    private static void MapCheckEndpoint(IEndpointRouteBuilder versionGroup) =>
        versionGroup.MapPost("/authorization/check", async (
            CheckPermissionRequest request,
            HttpContext context,
            [FromServices] IPermissionResolver resolver,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result validation = request.Validate();

            if (validation.IsFailure)
            {
                return validation.ToHttpResult(context, requestContext);
            }

            // A caller may only ask about themselves unless they hold the
            // permission to ask about others. Otherwise this endpoint would let
            // anyone enumerate the whole company's access.
            if (!TryGetUserId(context, out Guid callerId))
            {
                return Results.Unauthorized();
            }

            Guid subjectId = request.UserId ?? callerId;

            if (subjectId != callerId)
            {
                AccessDecision mayInspect = await resolver.EvaluateAsync(
                    callerId, "platform.authorization.inspect", cancellationToken);

                if (!mayInspect.IsGranted)
                {
                    return Results.Forbid();
                }
            }

            AccessDecision decision = await resolver.EvaluateAsync(
                subjectId, request.Permission, cancellationToken);

            // The filter travels with the answer, so a business application can
            // apply the same restriction to its own data (ADR-007 §14.3).
            return Results.Ok(new PermissionCheckDto(
                decision.IsGranted,
                decision.Scope.ToString(),
                decision.UnitPathPrefixes));
        })
            .RequireAuthorization()
            .WithMetadata(new AuthenticatedUserOnlyAttribute(
                "Checking one's own access needs no permission. Checking someone else's "
                + "requires platform.authorization.inspect, enforced inside the handler."))
            .WithTags("Authorization")
            .WithName("CheckPermission")
            .WithSummary("Answers whether a user holds a permission, and over what data.");

    private static void MapRoleEndpoints(IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder roles = versionGroup.MapGroup("/roles").WithTags("Roles");

        roles.MapGet("/", async (
            bool? includeInactive,
            HttpContext context,
            [FromServices] IAuthorizationRepository repository,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyList<Domain.Roles.Role> found =
                await repository.GetRolesAsync(includeInactive ?? false, cancellationToken);

            return Results.Ok(found.Select(r => new RoleDto(
                r.Id, r.Code, r.NameAr, r.NameEn, r.Description,
                r.IsSystem, r.IsActive, r.Permissions.Count)).ToArray());
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.roles.view"))
            .WithName("GetRoles")
            .WithSummary("Lists roles.");

        roles.MapGet("/permissions", async (
            bool? includeInactive,
            [FromServices] IAuthorizationRepository repository,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyList<Domain.Permissions.Permission> permissions =
                await repository.GetAllPermissionsAsync(includeInactive ?? false, cancellationToken);

            return Results.Ok(permissions.Select(p => new PermissionDto(
                p.Id, p.Name, p.Application, p.Resource, p.Action,
                p.Description, p.IsActive)).ToArray());
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.permissions.view"))
            .WithName("GetPermissions")
            .WithSummary("Lists every declared permission, including those of registered applications.");
    }

    private static void MapGrantEndpoints(IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder users = versionGroup.MapGroup("/users").WithTags("Roles");

        users.MapGet("/{id:guid}/roles", async (
            Guid id,
            [FromServices] IAuthorizationRepository repository,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyList<Domain.Roles.UserRoleAssignment> assignments =
                await repository.GetAssignmentsForUserAsync(id, cancellationToken);

            return Results.Ok(assignments.Select(a => new UserRoleDto(
                a.Id, a.UserId, a.RoleId, a.ScopeType.ToString(), a.ScopeUnitId,
                a.GrantedBy, a.GrantedAt, a.ExpiresAt, a.RevokedAt)).ToArray());
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.roles.view"))
            .WithName("GetUserRoles")
            .WithSummary("Lists a user's role assignments.");

        users.MapPost("/{id:guid}/roles", async (
            Guid id,
            GrantRoleRequest request,
            HttpContext context,
            [FromServices] GrantRoleHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result validation = request.Validate();

            if (validation.IsFailure)
            {
                return validation.ToHttpResult(context, requestContext);
            }

            // The granter comes from the token, never the body. Otherwise the
            // "you cannot grant what you do not hold" rule could be bypassed by
            // claiming to be someone who does.
            if (!TryGetUserId(context, out Guid actingUserId))
            {
                return Results.Unauthorized();
            }

            Result result = await handler.HandleAsync(
                new GrantRoleCommand(
                    id, request.RoleId, request.ParsedScope, request.ScopeUnitId,
                    request.ExpiresAt, actingUserId),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.roles.assign"))
            // Granting a role is the single most consequential action in the
            // Platform: it is how access is created. A stolen session must not
            // be able to grant itself more.
            .WithMetadata(new RequireStepUpAttribute())
            .WithName("GrantRole")
            .WithSummary("Grants a role at a scope, refusing any escalation.");

        users.MapDelete("/{id:guid}/roles/{assignmentId:guid}", async (
            Guid id,
            Guid assignmentId,
            HttpContext context,
            [FromServices] RevokeRoleHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(context, out Guid actingUserId))
            {
                return Results.Unauthorized();
            }

            Result result = await handler.HandleAsync(
                new RevokeRoleCommand(assignmentId, actingUserId), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.roles.assign"))
            // Revocation too: stripping an administrator's access is how an
            // intruder buys time before anyone can stop them.
            .WithMetadata(new RequireStepUpAttribute())
            .WithName("RevokeRole")
            .WithSummary("Revokes a role assignment. Takes effect on the next request.");
    }

    private static bool TryGetUserId(HttpContext context, out Guid userId)
    {
        string? subject = context.User.FindFirst("sub")?.Value;

        return Guid.TryParse(subject, out userId);
    }
}

/// <summary>Permission check request body.</summary>
/// <param name="Permission">The permission to check.</param>
/// <param name="UserId">
/// Whose access to check. Null means the caller's own; checking someone else's
/// requires <c>platform.authorization.inspect</c>.
/// </param>
public sealed record CheckPermissionRequest(string Permission, Guid? UserId)
{
    public Result Validate()
        => string.IsNullOrWhiteSpace(Permission)
            ? Result.Failure(Error.Validation(
                "AUTHZ.PERMISSION_REQUIRED", "A permission name is required.", "permission"))
            : Result.Success();
}

/// <summary>Grant request body.</summary>
public sealed record GrantRoleRequest(
    Guid RoleId,
    string Scope,
    Guid? ScopeUnitId,
    DateTimeOffset? ExpiresAt)
{
    /// <summary>The parsed scope. Only meaningful after <see cref="Validate"/> succeeds.</summary>
    public ScopeType ParsedScope { get; private set; }

    public Result Validate()
    {
        List<Error> errors = [];

        if (RoleId == Guid.Empty)
        {
            errors.Add(Error.Validation("AUTHZ.ROLE_REQUIRED", "A role is required.", "roleId"));
        }

        if (string.IsNullOrWhiteSpace(Scope) || !Enum.TryParse(Scope, ignoreCase: true, out ScopeType parsed))
        {
            errors.Add(Error.Validation(
                "AUTHZ.INVALID_SCOPE",
                $"Unknown scope. Allowed: {string.Join(", ", Enum.GetNames<ScopeType>())}.",
                "scope"));
        }
        else
        {
            ParsedScope = parsed;
        }

        return errors.Count == 0 ? Result.Success() : Result.Failure(errors);
    }
}
