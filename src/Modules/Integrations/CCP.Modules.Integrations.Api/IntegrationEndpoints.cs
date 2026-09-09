using System.Globalization;
using CCP.Kernel.Api.Context;
using CCP.Kernel.Api.Errors;
using CCP.Kernel.Api.Security;
using CCP.Kernel.Paging;
using CCP.Kernel.Results;
using CCP.Modules.Integrations.Application;
using CCP.Modules.Integrations.Application.Providers;
using CCP.Modules.Integrations.Application.Webhooks;
using CCP.Modules.Integrations.Contracts.Dtos;
using CCP.Modules.Integrations.Domain.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CCP.Modules.Integrations.Api;

/// <summary>
/// The integrations surface: administration, and the door webhooks come in
/// through.
/// <para>
/// <b>The webhook endpoint is anonymous and is not unauthenticated.</b> It is
/// authenticated by a signature over the body, which is the only credential a
/// provider posting from the internet has — and requiring a Platform token would
/// mean handing one to every external service, which is a far worse trade.
/// </para>
/// </summary>
public static class IntegrationEndpoints
{
    public static void MapIntegrationEndpoints(this IEndpointRouteBuilder versionGroup)
    {
        MapWebhookEndpoint(versionGroup);
        MapProviderEndpoints(versionGroup);
        MapCallLogEndpoints(versionGroup);
    }

    // -----------------------------------------------------------------------
    // Inbound
    // -----------------------------------------------------------------------

    private static void MapWebhookEndpoint(IEndpointRouteBuilder versionGroup) =>
        versionGroup.MapPost("/integrations/webhooks/{providerCode}", async (
            string providerCode,
            HttpContext context,
            [FromServices] ReceiveWebhookHandler handler,
            [FromServices] IntegrationOptions options,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            // Read whole and bounded. The signature is over the raw bytes, so
            // there is no way to verify it while streaming — which means an
            // unauthenticated caller would otherwise decide how much memory the
            // Platform allocates.
            context.Request.EnableBuffering();

            using var buffer = new MemoryStream();

            await context.Request.Body.CopyToAsync(buffer, cancellationToken);

            if (buffer.Length > options.MaxWebhookBodyBytes)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            long timestamp = long.TryParse(
                context.Request.Headers["X-CCP-Timestamp"].ToString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long parsed)
                ? parsed
                : 0;

            Result<ReceiveWebhookHandler.AcceptedWebhook> result = await handler.HandleAsync(
                new InboundWebhook(
                    providerCode,
                    context.Request.Headers["X-CCP-Signature"].ToString(),
                    timestamp,
                    buffer.ToArray()),
                cancellationToken);

            // Nothing about the payload comes back, not even on success. A
            // webhook endpoint that echoes is a webhook endpoint somebody will
            // use to find out what the Platform accepted.
            return result.IsSuccess
                ? Results.Accepted()
                : result.ToHttpResult(context, requestContext);
        })
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting(RateLimitPolicies.Anonymous)
            .WithTags("Integrations")
            .WithName("ReceiveWebhook")
            .WithSummary("Accepts a signed webhook from a registered provider.");

    // -----------------------------------------------------------------------
    // Administration
    // -----------------------------------------------------------------------

    private static void MapProviderEndpoints(IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder providers = versionGroup
            .MapGroup("/integrations/providers")
            .WithTags("Integrations");

        providers.MapGet("/", async (
            HttpContext context,
            [FromServices] GetProvidersHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<IntegrationProviderDto>> result =
                await handler.HandleAsync(cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.integrations.view"))
            .Produces<IReadOnlyList<IntegrationProviderDto>>(StatusCodes.Status200OK)
            .WithName("GetIntegrationProviders")
            .WithSummary("Every external service the Platform may call.");

        providers.MapPost("/", async (
            RegisterProviderRequest request,
            HttpContext context,
            [FromServices] RegisterProviderHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<IntegrationProviderDto> result = await handler.HandleAsync(
                new RegisterProviderCommand(request.Code, request.Name, request.BaseAddress),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.integrations.manage"))
            // Registering a provider decides where the Platform may send data,
            // and which credential travels with it.
            .WithMetadata(new RequireStepUpAttribute())
            .Produces<IntegrationProviderDto>(StatusCodes.Status200OK)
            .WithName("RegisterIntegrationProvider")
            .WithSummary("Registers an external service the Platform may call.");

        providers.MapPut("/{id:guid}", async (
            Guid id,
            ConfigureProviderRequest request,
            HttpContext context,
            [FromServices] ConfigureProviderHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<IntegrationProviderDto> result = await handler.HandleAsync(
                new ConfigureProviderCommand(
                    id,
                    request.TimeoutSeconds,
                    request.MaxRetries,
                    request.FailuresBeforeBreaking,
                    request.BreakDurationSeconds,
                    request.MaxConcurrentCalls,
                    request.RedactedFields ?? [],
                    request.CredentialReference),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.integrations.manage"))
            .Produces<IntegrationProviderDto>(StatusCodes.Status200OK)
            .WithName("ConfigureIntegrationProvider")
            .WithSummary("Sets resilience, redaction and the credential reference.");

        providers.MapPut("/{id:guid}/status", async (
            Guid id,
            SetProviderEnabledRequest request,
            HttpContext context,
            [FromServices] SetProviderEnabledHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.HandleAsync(
                new SetProviderEnabledCommand(id, request.IsEnabled), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.integrations.manage"))
            .WithName("SetIntegrationProviderStatus")
            .WithSummary("Stops calls to a provider, or starts them again.");

        providers.MapPost("/{id:guid}/endpoints", async (
            Guid id,
            AddEndpointRequest request,
            HttpContext context,
            [FromServices] AddEndpointHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<IntegrationEndpointDto> result = await handler.HandleAsync(
                new AddEndpointCommand(id, request.Key, request.Method, request.PathTemplate),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.integrations.manage"))
            .Produces<IntegrationEndpointDto>(StatusCodes.Status200OK)
            .WithName("AddIntegrationEndpoint")
            .WithSummary("Adds an operation to a provider.");

        providers.MapGet("/health", async (
            HttpContext context,
            [FromServices] GetProviderHealthHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<IntegrationHealthDto>> result =
                await handler.HandleAsync(cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.integrations.view"))
            .Produces<IReadOnlyList<IntegrationHealthDto>>(StatusCodes.Status200OK)
            .WithName("GetIntegrationHealth")
            .WithSummary("How each provider has been behaving, from its recent calls.");
    }

    // -----------------------------------------------------------------------
    // The call log
    // -----------------------------------------------------------------------

    private static void MapCallLogEndpoints(IEndpointRouteBuilder versionGroup) =>
        versionGroup.MapGet("/integrations/calls", async (
            string? providerCode,
            string? outcome,
            int? page,
            int? pageSize,
            HttpContext context,
            [FromServices] SearchCallLogHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<PageRequest> paging = PageRequest.Create(page, pageSize);

            if (paging.IsFailure)
            {
                return paging.ToHttpResult(context, requestContext);
            }

            CallOutcome? wanted =
                Enum.TryParse(outcome, ignoreCase: true, out CallOutcome parsed) ? parsed : null;

            Result<PagedResult<IntegrationCallDto>> result = await handler.HandleAsync(
                new SearchCallLogQuery(providerCode, wanted, paging.Value), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.integrations.view"))
            .Produces<PagedResult<IntegrationCallDto>>(StatusCodes.Status200OK)
            .WithTags("Integrations")
            .WithName("SearchIntegrationCalls")
            .WithSummary("What the Platform sent, and what came back.");
}

/// <summary>Registering an external service.</summary>
public sealed record RegisterProviderRequest(string Code, string Name, string BaseAddress);

/// <summary>
/// A provider's policy.
/// </summary>
/// <param name="CredentialReference">
/// The <b>name</b> of a secret, such as <c>integrations/acme/api-key</c>. A
/// value pasted here is refused: the Platform stores references, and the value
/// lives in the secret store (§19.3).
/// </param>
public sealed record ConfigureProviderRequest(
    int TimeoutSeconds,
    int MaxRetries,
    int FailuresBeforeBreaking,
    int BreakDurationSeconds,
    int MaxConcurrentCalls,
    IReadOnlyList<string>? RedactedFields,
    string? CredentialReference);

/// <summary>Turning a provider off, or back on.</summary>
public sealed record SetProviderEnabledRequest(bool IsEnabled);

/// <summary>Adding an operation.</summary>
public sealed record AddEndpointRequest(string Key, string Method, string PathTemplate);
