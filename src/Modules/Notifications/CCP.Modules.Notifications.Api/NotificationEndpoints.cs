using CCP.Kernel.Api.Context;
using CCP.Kernel.Api.Errors;
using CCP.Kernel.Api.Security;
using CCP.Kernel.Paging;
using CCP.Kernel.Results;
using CCP.Modules.Notifications.Application;
using CCP.Modules.Notifications.Contracts.Dtos;
using CCP.Modules.Notifications.Domain.Notifications;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CCP.Modules.Notifications.Api;

/// <summary>
/// The notifications surface.
/// <para>
/// Two audiences, kept apart. <b>People</b> read their own inbox and set their
/// own preferences, which needs no permission — gating one's own messages would
/// mean granting that permission to everybody. <b>Administrators</b> write
/// templates and look at what failed, which does.
/// </para>
/// </summary>
public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder versionGroup)
    {
        MapInboxEndpoints(versionGroup);
        MapPreferenceEndpoints(versionGroup);
        MapAdministrationEndpoints(versionGroup);
    }

    private static void MapInboxEndpoints(IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder inbox = versionGroup
            .MapGroup("/me/notifications")
            .WithTags("Notifications");

        inbox.MapGet("/", async (
            bool? unreadOnly,
            int? page,
            int? pageSize,
            HttpContext context,
            [FromServices] GetInboxHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            if (!CallerIdentity.TryGetUserId(context.User, out Guid userId))
            {
                return Results.Unauthorized();
            }

            Result<PagedResult<NotificationDto>> result = await handler.HandleAsync(
                new GetInboxQuery(userId, unreadOnly ?? false, page ?? 1, pageSize ?? 25),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new AuthenticatedUserOnlyAttribute(
                "Reading one's own messages needs no permission. Gating it would mean "
                + "granting that permission to everybody."))
            .Produces<PagedResult<NotificationDto>>(StatusCodes.Status200OK)
            .WithName("GetMyNotifications")
            .WithSummary("The caller's in-app inbox.");

        inbox.MapPost("/{id:guid}/read", async (
            Guid id,
            HttpContext context,
            [FromServices] MarkReadHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            // The reader comes from the token. The aggregate refuses a mismatch,
            // so an endpoint that took a user id would be one refactor away from
            // letting somebody mark another person's messages read — and the
            // unread count is the only signal that person has.
            if (!CallerIdentity.TryGetUserId(context.User, out Guid readerUserId))
            {
                return Results.Unauthorized();
            }

            Result result = await handler.HandleAsync(
                new MarkReadCommand(id, readerUserId), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new AuthenticatedUserOnlyAttribute(
                "Marking one's own message read is authorised by being its recipient, "
                + "which the aggregate checks."))
            .WithName("MarkNotificationRead")
            .WithSummary("Marks one of the caller's own notifications as read.");
    }

    private static void MapPreferenceEndpoints(IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder preferences = versionGroup
            .MapGroup("/me/notification-preferences")
            .WithTags("Notifications");

        preferences.MapGet("/", async (
            HttpContext context,
            [FromServices] GetPreferencesHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            if (!CallerIdentity.TryGetUserId(context.User, out Guid userId))
            {
                return Results.Unauthorized();
            }

            Result<IReadOnlyList<NotificationPreferenceDto>> result =
                await handler.HandleAsync(new GetPreferencesQuery(userId), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new AuthenticatedUserOnlyAttribute(
                "One's own preferences are one's own."))
            .Produces<IReadOnlyList<NotificationPreferenceDto>>(StatusCodes.Status200OK)
            .WithName("GetMyNotificationPreferences")
            .WithSummary("What the caller has turned off. Absence means everything is on.");

        preferences.MapPut("/", async (
            SetPreferenceRequest request,
            HttpContext context,
            [FromServices] SetPreferenceHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result validation = request.Validate();

            if (validation.IsFailure)
            {
                return validation.ToHttpResult(context, requestContext);
            }

            if (!CallerIdentity.TryGetUserId(context.User, out Guid userId))
            {
                return Results.Unauthorized();
            }

            Result result = await handler.HandleAsync(
                new SetPreferenceCommand(
                    userId, request.Category, request.ParsedChannel, request.IsEnabled),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new AuthenticatedUserOnlyAttribute(
                "Choosing what to receive is a personal setting, not a privilege."))
            .WithName("SetMyNotificationPreference")
            .WithSummary("Turns a category on or off. Security notifications are refused.");
    }

    private static void MapAdministrationEndpoints(IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder notifications = versionGroup
            .MapGroup("/notifications")
            .WithTags("Notifications");

        notifications.MapGet("/", async (
            string? status,
            string? category,
            string? channel,
            int? page,
            int? pageSize,
            HttpContext context,
            [FromServices] SearchNotificationsHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            NotificationStatus? wantedStatus = null;

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse(status, ignoreCase: true, out NotificationStatus parsed))
                {
                    return Result.Failure(Error.Validation(
                        "NOTIFICATIONS.UNKNOWN_STATUS", "That is not a status.", "status"))
                        .ToHttpResult(context, requestContext);
                }

                wantedStatus = parsed;
            }

            NotificationChannel? wantedChannel = null;

            if (!string.IsNullOrWhiteSpace(channel))
            {
                if (!Enum.TryParse(channel, ignoreCase: true, out NotificationChannel parsed))
                {
                    return Result.Failure(Error.Validation(
                        "NOTIFICATIONS.UNKNOWN_CHANNEL", "That is not a channel.", "channel"))
                        .ToHttpResult(context, requestContext);
                }

                wantedChannel = parsed;
            }

            Result<PagedResult<NotificationDto>> result = await handler.HandleAsync(
                new SearchNotificationsQuery(
                    wantedStatus, category, wantedChannel, page ?? 1, pageSize ?? 25),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.notifications.view"))
            .Produces<PagedResult<NotificationDto>>(StatusCodes.Status200OK)
            .WithName("SearchNotifications")
            .WithSummary("Everything sent, with every delivery attempt. Failures surface here.");

        RouteGroupBuilder templates = versionGroup
            .MapGroup("/notifications/templates")
            .WithTags("Notifications");

        templates.MapGet("/", async (
            bool? includeInactive,
            HttpContext context,
            [FromServices] GetTemplatesHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<NotificationTemplateDto>> result = await handler.HandleAsync(
                new GetTemplatesQuery(includeInactive ?? false), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.notifications.manage"))
            .Produces<IReadOnlyList<NotificationTemplateDto>>(StatusCodes.Status200OK)
            .WithName("GetNotificationTemplates")
            .WithSummary("The template catalogue, one row per message per language.");

        templates.MapPut("/", async (
            SaveTemplateRequest request,
            HttpContext context,
            [FromServices] SaveTemplateHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result validation = request.Validate();

            if (validation.IsFailure)
            {
                return validation.ToHttpResult(context, requestContext);
            }

            Result<NotificationTemplateDto> result = await handler.HandleAsync(
                new SaveTemplateCommand(
                    request.Code, request.Locale, request.Subject ?? string.Empty,
                    request.Body, request.Variables ?? []),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.notifications.manage"))
            .Produces<NotificationTemplateDto>(StatusCodes.Status200OK)
            .WithName("SaveNotificationTemplate")
            .WithSummary("Writes or revises a template. Revising bumps its version.");
    }
}

// ---------------------------------------------------------------------------
// Request bodies
// ---------------------------------------------------------------------------

/// <summary>Set-preference request body.</summary>
public sealed record SetPreferenceRequest(string Category, string Channel, bool IsEnabled)
{
    /// <summary>Only meaningful after <see cref="Validate"/> succeeds.</summary>
    public NotificationChannel ParsedChannel { get; private set; }

    public Result Validate()
    {
        if (string.IsNullOrWhiteSpace(Category))
        {
            return Result.Failure(Error.Validation(
                "NOTIFICATIONS.CATEGORY_REQUIRED", "Say which category.", "category"));
        }

        if (!Enum.TryParse(Channel, ignoreCase: true, out NotificationChannel parsed))
        {
            return Result.Failure(Error.Validation(
                "NOTIFICATIONS.UNKNOWN_CHANNEL",
                $"'{Channel}' is not a channel the Platform delivers on.",
                "channel"));
        }

        ParsedChannel = parsed;

        return Result.Success();
    }
}

/// <summary>Save-template request body.</summary>
public sealed record SaveTemplateRequest(
    string Code,
    string Locale,
    string Body,
    string? Subject = null,
    IReadOnlyList<string>? Variables = null)
{
    public Result Validate()
        => string.IsNullOrWhiteSpace(Code)
        || string.IsNullOrWhiteSpace(Locale)
        || string.IsNullOrWhiteSpace(Body)
            ? Result.Failure(Error.Validation(
                "NOTIFICATIONS.TEMPLATE_INCOMPLETE",
                "A template needs a code, a language and a body.",
                "code"))
            : Result.Success();
}
