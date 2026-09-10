using CCP.Kernel.Api.Context;
using CCP.Kernel.Api.Errors;
using CCP.Kernel.Api.Security;
using CCP.Kernel.Results;
using CCP.Modules.Configuration.Application;
using CCP.Modules.Configuration.Application.Abstractions;
using CCP.Modules.Configuration.Contracts.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CCP.Modules.Configuration.Api;

/// <summary>
/// Settings, their history, and the switches.
/// <para>
/// <b>There is no endpoint that reads one setting's value.</b> Reading the list
/// returns what is set, with sensitive values absent from the shape — and a
/// single-value endpoint would be the obvious place for somebody to later add a
/// convenience that returns them. The Platform reads its own settings through
/// <c>IConfigurationReader</c>, in process, which no HTTP caller can reach.
/// </para>
/// </summary>
public static class ConfigurationEndpoints
{
    public static void MapConfigurationEndpoints(this IEndpointRouteBuilder versionGroup)
    {
        MapSettingEndpoints(versionGroup);
        MapFlagEndpoints(versionGroup);
    }

    private static void MapSettingEndpoints(IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder settings = versionGroup
            .MapGroup("/configuration/settings")
            .WithTags("Configuration");

        settings.MapGet("/", async (
            string? applicationCode,
            HttpContext context,
            [FromServices] GetSettingsHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<SettingDto>> result =
                await handler.HandleAsync(applicationCode, cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.configuration.view"))
            .Produces<IReadOnlyList<SettingDto>>(StatusCodes.Status200OK)
            .WithName("GetSettings")
            .WithSummary("Every declared setting, and what has been set for it.");

        settings.MapPut("/declare", async (
            DeclareSettingRequest request,
            HttpContext context,
            [FromServices] DeclareSettingHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            // The namespace comes from the token when an application is calling,
            // exactly as the permission manifest does — so a system can only
            // ever declare settings it owns. A person supplies it, because a
            // person may declare on behalf of the Platform itself.
            string? applicationCode =
                context.User.FindFirst(CallerIdentity.ApplicationCodeClaim)?.Value
                ?? request.ApplicationCode;

            if (string.IsNullOrWhiteSpace(applicationCode))
            {
                return Results.BadRequest();
            }

            Result<SettingDto> result = await handler.HandleAsync(
                new DeclareSettingCommand(
                    request.Key,
                    applicationCode,
                    request.ValueType,
                    request.Description,
                    request.DefaultValue,
                    request.IsSensitive ?? false,
                    request.Minimum,
                    request.Maximum,
                    request.AllowedValues),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.configuration.manage"))
            .Produces<SettingDto>(StatusCodes.Status200OK)
            .WithName("DeclareSetting")
            .WithSummary("Declares a setting, or updates its description and constraints.");

        settings.MapPut("/value", async (
            SetSettingRequest request,
            HttpContext context,
            [FromServices] SetSettingHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            if (!CallerIdentity.TryGetUserId(context.User, out Guid actingUserId))
            {
                return Results.Unauthorized();
            }

            Result result = await handler.HandleAsync(
                new SetSettingCommand(
                    request.Key,
                    request.Scope,
                    request.ScopeId,
                    request.Value,
                    request.Reason,
                    actingUserId),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.configuration.manage"))
            .WithName("SetSetting")
            .WithSummary("Sets a value at a scope, or clears the override.");

        settings.MapGet("/{key}/history", async (
            string key,
            HttpContext context,
            [FromServices] GetSettingHistoryHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<SettingChangeDto>> result =
                await handler.HandleAsync(key, cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.configuration.view"))
            .Produces<IReadOnlyList<SettingChangeDto>>(StatusCodes.Status200OK)
            .WithName("GetSettingHistory")
            .WithSummary("What this setting was, what it became, who changed it and when.");
    }

    private static void MapFlagEndpoints(IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder flags = versionGroup
            .MapGroup("/configuration/flags")
            .WithTags("Configuration");

        flags.MapGet("/", async (
            string? applicationCode,
            HttpContext context,
            [FromServices] GetFlagsHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<FeatureFlagDto>> result =
                await handler.HandleAsync(applicationCode, cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.configuration.view"))
            .Produces<IReadOnlyList<FeatureFlagDto>>(StatusCodes.Status200OK)
            .WithName("GetFeatureFlags")
            .WithSummary("Every declared flag and its switch.");

        flags.MapPut("/declare", async (
            DeclareFlagRequest request,
            HttpContext context,
            [FromServices] DeclareFlagHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            string? applicationCode =
                context.User.FindFirst(CallerIdentity.ApplicationCodeClaim)?.Value
                ?? request.ApplicationCode;

            if (string.IsNullOrWhiteSpace(applicationCode))
            {
                return Results.BadRequest();
            }

            Result<FeatureFlagDto> result = await handler.HandleAsync(
                new DeclareFlagCommand(request.Key, applicationCode, request.Description),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.configuration.manage"))
            .Produces<FeatureFlagDto>(StatusCodes.Status200OK)
            .WithName("DeclareFeatureFlag")
            .WithSummary("Declares a flag. It is off until somebody turns it on.");

        flags.MapPut("/{key}", async (
            string key,
            SetFlagRequest request,
            HttpContext context,
            [FromServices] SetFlagHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            if (!CallerIdentity.TryGetUserId(context.User, out Guid actingUserId))
            {
                return Results.Unauthorized();
            }

            Result<FeatureFlagDto> result = await handler.HandleAsync(
                new SetFlagCommand(
                    key, request.IsEnabled, request.RoleIds, request.UnitIds, actingUserId),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.configuration.manage"))
            .Produces<FeatureFlagDto>(StatusCodes.Status200OK)
            .WithName("SetFeatureFlag")
            .WithSummary("Turns a flag on or off, and decides who it reaches.");

        versionGroup.MapGet("/me/features/{key}", async (
            string key,
            HttpContext context,
            [FromServices] IConfigurationReader reader,
            [FromServices] IFeatureSubjectResolver subjects,
            CancellationToken cancellationToken) =>
        {
            // What a screen asks before deciding whether to render something.
            // Needs no permission: it answers a question about the caller, and
            // gating it would mean granting that permission to everybody.
            if (!CallerIdentity.TryGetUserId(context.User, out Guid userId))
            {
                return Results.Unauthorized();
            }

            // The caller's roles and unit ancestry, resolved rather than
            // assumed empty. This endpoint shipped passing two empty lists to an
            // evaluator that reads them, so every *targeted* flag answered "off"
            // to everybody who asked through the API — a rollout aimed at one
            // department never arrived, nothing failed, and the screen asking
            // had no way to tell that from a flag which was genuinely off.
            FeatureSubject subject = await subjects.ResolveAsync(userId, cancellationToken);

            bool isOn = await reader.IsFeatureOnAsync(
                key, subject.RoleIds, subject.UnitChainIds, cancellationToken);

            return Results.Ok(new FeatureStateDto(key, isOn));
        })
            .RequireAuthorization()
            .WithMetadata(new AuthenticatedUserOnlyAttribute(
                "It answers a question about the caller themselves. Gating it behind a "
                + "permission would mean granting that permission to everybody, which makes "
                + "the permission meaningless."))
            .Produces<FeatureStateDto>(StatusCodes.Status200OK)
            .WithTags("Configuration")
            .WithName("GetMyFeatureState")
            .WithSummary("Whether a feature is on for the caller.");
    }
}

/// <summary>Declaring a setting.</summary>
public sealed record DeclareSettingRequest(
    string Key,
    string? ApplicationCode,
    string ValueType,
    string? Description,
    string? DefaultValue,
    bool? IsSensitive,
    long? Minimum,
    long? Maximum,
    IReadOnlyList<string>? AllowedValues);

/// <summary>
/// Setting a value.
/// </summary>
/// <param name="Value">
/// The new value, or null to clear the override so the setting falls back to the
/// scope above.
/// </param>
public sealed record SetSettingRequest(
    string Key, string Scope, Guid? ScopeId, string? Value, string? Reason);

/// <summary>Declaring a flag.</summary>
public sealed record DeclareFlagRequest(string Key, string? ApplicationCode, string? Description);

/// <summary>Turning a flag on or off.</summary>
public sealed record SetFlagRequest(
    bool IsEnabled, IReadOnlyList<Guid>? RoleIds, IReadOnlyList<Guid>? UnitIds);
