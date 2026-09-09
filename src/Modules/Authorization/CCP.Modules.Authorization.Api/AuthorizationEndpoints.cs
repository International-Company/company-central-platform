using CCP.Kernel.Api.Context;
using CCP.Kernel.Api.Errors;
using CCP.Kernel.Api.Security;
using CCP.Kernel.Results;
using CCP.Modules.Authorization.Application.Abstractions;
using CCP.Modules.Authorization.Application.Grants;
using CCP.Modules.Authorization.Application.Roles;
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
            .Produces<MyPermissionsDto>(StatusCodes.Status200OK)
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
            // Declares what a success returns, so the OpenAPI document describes
            // the response and not merely the request.
            .Produces<IReadOnlyList<RoleDto>>(StatusCodes.Status200OK)
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
            .Produces<IReadOnlyList<PermissionDto>>(StatusCodes.Status200OK)
            .WithName("GetPermissions")
            .WithSummary("Lists every declared permission, including those of registered applications.");

        roles.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext context,
            [FromServices] IAuthorizationRepository repository,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Domain.Roles.Role? role = await repository.FindRoleAsync(id, cancellationToken);

            if (role is null)
            {
                return Result.Failure(Domain.AuthorizationErrors.RoleNotFound)
                    .ToHttpResult(context, requestContext);
            }

            // The permission ids, not merely how many. A screen editing what a
            // role grants has to start from what it already grants; starting
            // from empty would turn every save into a silent wipe.
            return Results.Ok(new RoleDetailDto(
                role.Id, role.Code, role.NameAr, role.NameEn, role.Description,
                role.IsSystem, role.IsActive,
                [.. role.Permissions.Select(p => p.PermissionId)]));
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.roles.view"))
            .Produces<RoleDetailDto>(StatusCodes.Status200OK)
            .WithName("GetRole")
            .WithSummary("Returns one role with the permissions it carries.");

        // --------------------------------------------------------------------
        // Defining roles
        // --------------------------------------------------------------------
        // Separate from assigning them, and behind a separate permission.
        // Deciding what a bundle of access contains and deciding who receives it
        // are different jobs with different blast radii, and one person holding
        // both is a choice a company should make rather than one this Platform
        // makes for them.
        //
        // Until these existed the Platform had one role, holding everything, so
        // granting anybody anything made them a full administrator.

        roles.MapPost("/", async (
            CreateRoleRequest request,
            HttpContext context,
            [FromServices] CreateRoleHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result validation = request.Validate();

            if (validation.IsFailure)
            {
                return validation.ToHttpResult(context, requestContext);
            }

            Result<RoleDto> result = await handler.HandleAsync(
                new CreateRoleCommand(
                    request.Code, request.NameAr, request.NameEn, request.Description),
                cancellationToken);

            return result.IsSuccess
                ? result.ToCreatedResult(
                    $"/api/v1/roles/{result.Value.Id}", context, requestContext)
                : result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.roles.manage"))
            .Produces<RoleDto>(StatusCodes.Status201Created)
            .WithName("CreateRole")
            .WithSummary("Creates a role. It starts empty; its permissions are set separately.");

        roles.MapPut("/{id:guid}", async (
            Guid id,
            UpdateRoleRequest request,
            HttpContext context,
            [FromServices] UpdateRoleHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result validation = request.Validate();

            if (validation.IsFailure)
            {
                return validation.ToHttpResult(context, requestContext);
            }

            Result<RoleDto> result = await handler.HandleAsync(
                new UpdateRoleCommand(
                    id, request.NameAr, request.NameEn, request.Description),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.roles.manage"))
            .Produces<RoleDto>(StatusCodes.Status200OK)
            .WithName("UpdateRole")
            .WithSummary("Renames a role. The code is fixed once created.");

        roles.MapPut("/{id:guid}/permissions", async (
            Guid id,
            SetRolePermissionsRequest request,
            HttpContext context,
            [FromServices] SetRolePermissionsHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            // The acting user comes from the token, never the body. The rule
            // being enforced is "you may not put in a permission you do not
            // hold", and a caller who could name themselves would be exempt
            // from it.
            if (!TryGetUserId(context, out Guid actingUserId))
            {
                return Results.Unauthorized();
            }

            Result<RoleDto> result = await handler.HandleAsync(
                new SetRolePermissionsCommand(id, request.PermissionIds, actingUserId),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.roles.manage"))
            // Changing what a role grants changes what everyone holding it can
            // do, without touching a single assignment. That is as consequential
            // as a grant, so it is protected the same way.
            .WithMetadata(new RequireStepUpAttribute())
            .Produces<RoleDto>(StatusCodes.Status200OK)
            .WithName("SetRolePermissions")
            .WithSummary("Replaces the permissions a role carries, refusing any the caller does not hold.");

        roles.MapPost("/{id:guid}/status", async (
            Guid id,
            SetRoleActiveRequest request,
            HttpContext context,
            [FromServices] SetRoleActiveHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.HandleAsync(
                new SetRoleActiveCommand(id, request.IsActive), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.roles.manage"))
            .WithName("SetRoleStatus")
            .WithSummary("Deactivates a role, or brings it back. Never deletes one.");
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
            .Produces<IReadOnlyList<UserRoleDto>>(StatusCodes.Status200OK)
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

    /// <summary>
    /// The caller, from the kernel's single reader.
    /// <para>
    /// This method used to read only <c>sub</c>, which the JWT handler had
    /// already renamed — so it answered "no caller" for every authenticated
    /// request, and these endpoints returned 401 to people holding good tokens.
    /// Reading one's own permissions was one of them, which is why the portal
    /// hid every control it has.
    /// </para>
    /// </summary>
    private static bool TryGetUserId(HttpContext context, out Guid userId)
        => CallerIdentity.TryGetUserId(context.User, out userId);
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
/// <summary>Create-role request body.</summary>
public sealed record CreateRoleRequest(
    string Code,
    string NameAr,
    string NameEn,
    string? Description = null)
{
    public Result Validate()
        => string.IsNullOrWhiteSpace(Code)
        || string.IsNullOrWhiteSpace(NameAr)
        || string.IsNullOrWhiteSpace(NameEn)
            ? Result.Failure(Error.Validation(
                "AUTHORIZATION.ROLE_INCOMPLETE",
                "A role needs a code and a name in both languages.",
                "code"))
            : Result.Success();
}

/// <summary>Rename-role request body. The code is absent because it never changes.</summary>
public sealed record UpdateRoleRequest(
    string NameAr,
    string NameEn,
    string? Description = null)
{
    public Result Validate()
        => string.IsNullOrWhiteSpace(NameAr) || string.IsNullOrWhiteSpace(NameEn)
            ? Result.Failure(Error.Validation(
                "AUTHORIZATION.ROLE_NAME_REQUIRED",
                "A role needs a name in both languages.",
                "nameAr"))
            : Result.Success();
}

/// <summary>
/// The permissions a role should carry, in full.
/// <para>
/// The whole set, not a change to it. "These are the permissions" is a state
/// the caller can see and reason about; "add this one" is a sequence of edits
/// whose result nobody can predict from any single request.
/// </para>
/// </summary>
public sealed record SetRolePermissionsRequest(IReadOnlyList<Guid> PermissionIds);

/// <summary>Whether a role should be active.</summary>
public sealed record SetRoleActiveRequest(bool IsActive);

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
