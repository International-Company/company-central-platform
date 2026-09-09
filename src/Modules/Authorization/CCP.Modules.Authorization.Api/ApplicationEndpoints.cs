using CCP.Kernel.Api.Context;
using CCP.Kernel.Api.Errors;
using CCP.Kernel.Api.Security;
using CCP.Kernel.Results;
using CCP.Modules.Authorization.Application.Applications;
using CCP.Modules.Authorization.Contracts.Dtos;
using CCP.Modules.Authorization.Domain.Scopes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CCP.Modules.Authorization.Api;

/// <summary>
/// The application registry, and the door machines come in through.
/// <para>
/// Two audiences with nothing in common. <b>The token endpoint is anonymous</b> —
/// it is where a caller with no token gets one, so requiring authorization on it
/// would be circular. Everything else is administration and needs a person
/// holding <c>platform.applications.manage</c>.
/// </para>
/// </summary>
public static class ApplicationEndpoints
{
    public static void MapApplicationEndpoints(this IEndpointRouteBuilder versionGroup)
    {
        MapTokenEndpoint(versionGroup);
        MapManifestEndpoint(versionGroup);
        MapRegistryEndpoints(versionGroup);
        MapCredentialEndpoints(versionGroup);
        MapApplicationRoleEndpoints(versionGroup);
    }

    // -----------------------------------------------------------------------
    // The permission manifest
    // -----------------------------------------------------------------------

    /// <summary>
    /// An application declaring what its own permissions are.
    /// <para>
    /// <b>The namespace comes from the token, not from the request.</b> There is
    /// no path parameter and no field naming an application, so a caller can
    /// only ever declare its own namespace — the restriction is structural
    /// rather than a check somebody has to remember to write.
    /// </para>
    /// <para>
    /// No permission beyond being a registered application, and that is a
    /// deliberate call. Declared permissions do nothing at all until an
    /// administrator puts them in a role and grants it: the blast radius is rows
    /// in a table. Requiring a grant first would mean two administrator actions
    /// to onboard every system, and onboarding friction is what drives teams to
    /// build their own thing instead (ADR-012).
    /// </para>
    /// </summary>
    private static void MapManifestEndpoint(IEndpointRouteBuilder versionGroup) =>
        versionGroup.MapPut("/applications/permissions", async (
            DeclarePermissionsRequest request,
            HttpContext context,
            [FromServices] DeclarePermissionsHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            string? applicationCode =
                context.User.FindFirst(CallerIdentity.ApplicationCodeClaim)?.Value;

            if (string.IsNullOrWhiteSpace(applicationCode))
            {
                // A person's token has no namespace to declare into. A manifest
                // is the owning system's statement about itself, and there is no
                // sensible way for an administrator to make it on their behalf.
                return Results.Forbid();
            }

            Result<PermissionDeclarationResult> result = await handler.HandleAsync(
                new DeclarePermissionsCommand(
                    applicationCode,
                    [.. request.Permissions.Select(p =>
                        new PermissionDeclaration(p.Name, p.Description))]),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new AuthenticatedUserOnlyAttribute(
                "An application declares its own namespace and no other. The namespace comes "
                + "from the token rather than the request, so there is nothing a permission "
                + "would additionally restrict — and declared permissions grant nobody "
                + "anything until an administrator puts them in a role."))
            .Produces<PermissionDeclarationResult>(StatusCodes.Status200OK)
            .WithTags("Applications")
            .WithName("DeclareApplicationPermissions")
            .WithSummary("Records an application's complete permission manifest.");

    // -----------------------------------------------------------------------
    // The token endpoint
    // -----------------------------------------------------------------------

    private static void MapTokenEndpoint(IEndpointRouteBuilder versionGroup) =>
        versionGroup.MapPost("/oauth/token", async (
            HttpContext context,
            [FromServices] MachineTokenHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            // Form-encoded, because RFC 6749 §4.4 says so and because every
            // OAuth client library in existence already sends it that way. A
            // JSON-only token endpoint is a Platform that every client has to
            // write a special case for.
            if (!context.Request.HasFormContentType)
            {
                return Results.BadRequest(new
                {
                    error = "invalid_request",
                    error_description = "Send credentials as application/x-www-form-urlencoded."
                });
            }

            IFormCollection form = await context.Request.ReadFormAsync(cancellationToken);

            if (form["grant_type"].ToString() != "client_credentials")
            {
                return Results.BadRequest(new
                {
                    error = "unsupported_grant_type",
                    error_description = "Only client_credentials is supported."
                });
            }

            Guid? onBehalfOf = Guid.TryParse(form["on_behalf_of"], out Guid subject)
                ? subject
                : null;

            Result<MachineTokenDto> result = await handler.HandleAsync(
                new MachineTokenCommand(
                    form["client_id"].ToString(),
                    form["client_secret"].ToString(),
                    onBehalfOf),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting(RateLimitPolicies.MachineToken)
            .Produces<MachineTokenDto>(StatusCodes.Status200OK)
            .WithTags("Applications")
            .WithName("IssueMachineToken")
            .WithSummary("Exchanges client credentials for a short-lived access token.");

    // -----------------------------------------------------------------------
    // The registry
    // -----------------------------------------------------------------------

    private static void MapRegistryEndpoints(IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder applications = versionGroup
            .MapGroup("/applications")
            .WithTags("Applications");

        applications.MapGet("/", async (
            HttpContext context,
            [FromServices] GetApplicationsHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<RegisteredApplicationDto>> result =
                await handler.HandleAsync(cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.applications.view"))
            .Produces<IReadOnlyList<RegisteredApplicationDto>>(StatusCodes.Status200OK)
            .WithName("GetApplications")
            .WithSummary("Every system registered to call this Platform.");

        applications.MapPost("/", async (
            RegisterApplicationRequest request,
            HttpContext context,
            [FromServices] RegisterApplicationHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<RegisteredApplicationDto> result = await handler.HandleAsync(
                new RegisterApplicationCommand(request.Code, request.Name, request.Description),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.applications.manage"))
            // Registering an application hands out a permission namespace and
            // the ability to hold roles. It is the machine equivalent of
            // creating an administrator.
            .WithMetadata(new RequireStepUpAttribute())
            .Produces<RegisteredApplicationDto>(StatusCodes.Status200OK)
            .WithName("RegisterApplication")
            .WithSummary("Registers a system that will call the Platform.");

        applications.MapPut("/{id:guid}/status", async (
            Guid id,
            SetApplicationStatusRequest request,
            HttpContext context,
            [FromServices] SetApplicationStatusHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.HandleAsync(
                new SetApplicationStatusCommand(id, request.IsActive), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.applications.manage"))
            .WithName("SetApplicationStatus")
            .WithSummary("Disables an application, or brings it back.");
    }

    // -----------------------------------------------------------------------
    // Credentials
    // -----------------------------------------------------------------------

    private static void MapCredentialEndpoints(IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder credentials = versionGroup
            .MapGroup("/applications/{id:guid}/credentials")
            .WithTags("Applications");

        credentials.MapGet("/", async (
            Guid id,
            HttpContext context,
            [FromServices] GetCredentialsHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<ApplicationCredentialDto>> result =
                await handler.HandleAsync(id, cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.applications.view"))
            .Produces<IReadOnlyList<ApplicationCredentialDto>>(StatusCodes.Status200OK)
            .WithName("GetApplicationCredentials")
            .WithSummary("An application's keys, including the withdrawn ones.");

        credentials.MapPost("/", async (
            Guid id,
            IssueCredentialRequest request,
            HttpContext context,
            [FromServices] IssueCredentialHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<IssuedCredentialDto> result = await handler.HandleAsync(
                new IssueCredentialCommand(id, request.Label, request.ExpiresAt),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.applications.manage"))
            // A client secret can act with no person present, for as long as
            // nobody revokes it. A second factor is the least it should cost.
            .WithMetadata(new RequireStepUpAttribute())
            .Produces<IssuedCredentialDto>(StatusCodes.Status200OK)
            .WithName("IssueApplicationCredential")
            .WithSummary("Mints a client secret. The secret is in this response and nowhere else.");

        credentials.MapDelete("/{credentialId:guid}", async (
            Guid id,
            Guid credentialId,
            HttpContext context,
            [FromServices] RevokeCredentialHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            if (!CallerIdentity.TryGetUserId(context.User, out Guid actingUserId))
            {
                return Results.Unauthorized();
            }

            Result result = await handler.HandleAsync(
                new RevokeCredentialCommand(id, credentialId), actingUserId, cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.applications.manage"))
            .WithName("RevokeApplicationCredential")
            .WithSummary("Stops a credential working, immediately.");
    }

    // -----------------------------------------------------------------------
    // What an application may do
    // -----------------------------------------------------------------------

    private static void MapApplicationRoleEndpoints(IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder roles = versionGroup
            .MapGroup("/applications/{id:guid}/roles")
            .WithTags("Applications");

        roles.MapGet("/", async (
            Guid id,
            HttpContext context,
            [FromServices] GetApplicationRolesHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<ApplicationRoleDto>> result =
                await handler.HandleAsync(id, cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.applications.view"))
            .Produces<IReadOnlyList<ApplicationRoleDto>>(StatusCodes.Status200OK)
            .WithName("GetApplicationRoles")
            .WithSummary("What this application is allowed to do.");

        roles.MapPost("/", async (
            Guid id,
            GrantApplicationRoleRequest request,
            HttpContext context,
            [FromServices] GrantApplicationRoleHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            if (!CallerIdentity.TryGetUserId(context.User, out Guid actingUserId))
            {
                return Results.Unauthorized();
            }

            if (!Enum.TryParse(request.Scope, ignoreCase: true, out ScopeType scope))
            {
                return Results.BadRequest();
            }

            Result result = await handler.HandleAsync(
                new GrantApplicationRoleCommand(
                    id, request.RoleId, scope, request.ScopeUnitId, request.ExpiresAt, actingUserId),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.applications.manage"))
            // Granting a role to a machine gives it to something that never
            // sleeps and never notices it has been compromised.
            .WithMetadata(new RequireStepUpAttribute())
            .WithName("GrantApplicationRole")
            .WithSummary("Gives an application a role, at a scope.");

        roles.MapDelete("/{assignmentId:guid}", async (
            Guid id,
            Guid assignmentId,
            HttpContext context,
            [FromServices] RevokeApplicationRoleHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            if (!CallerIdentity.TryGetUserId(context.User, out Guid actingUserId))
            {
                return Results.Unauthorized();
            }

            Result result = await handler.HandleAsync(
                new RevokeApplicationRoleCommand(id, assignmentId, actingUserId), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.applications.manage"))
            .WithName("RevokeApplicationRole")
            .WithSummary("Takes a role away from an application.");
    }
}

/// <summary>Registering a system.</summary>
public sealed record RegisterApplicationRequest(string Code, string Name, string? Description);

/// <summary>Turning one off, or back on.</summary>
public sealed record SetApplicationStatusRequest(bool IsActive);

/// <summary>Minting a client secret.</summary>
public sealed record IssueCredentialRequest(string Label, DateTimeOffset? ExpiresAt);

/// <summary>Granting an application a role.</summary>
public sealed record GrantApplicationRoleRequest(
    Guid RoleId, string Scope, Guid? ScopeUnitId, DateTimeOffset? ExpiresAt);

/// <summary>
/// An application's complete permission manifest.
/// <para>
/// Complete, and sent on every startup. The Platform reconciles: names that
/// disappear are deactivated, not deleted, because roles still reference them
/// and audit records from last year still name them. Incremental add-and-remove
/// calls drift the moment one fails, and nobody notices until a permission check
/// does the wrong thing in production.
/// </para>
/// </summary>
public sealed record DeclarePermissionsRequest(
    IReadOnlyList<PermissionDeclarationRequest> Permissions);

/// <summary>One permission an application declares.</summary>
public sealed record PermissionDeclarationRequest(string Name, string? Description);
