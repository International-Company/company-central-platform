using System.Security.Claims;
using CCP.Kernel.Api.Context;
using CCP.Kernel.Api.Errors;
using CCP.Kernel.Api.Security;
using CCP.Kernel.Paging;
using CCP.Kernel.Results;
using CCP.Modules.Audit.Application;
using CCP.Modules.Audit.Contracts.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace CCP.Modules.Audit.Api;

/// <summary>
/// Reading the trail, and letting other systems write to it
/// (ARCHITECTURE.md §15.3, §15.5).
/// </summary>
public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder audit = versionGroup.MapGroup("/audit").WithTags("Audit");

        audit.MapGet("/events", async (
            [AsParameters] AuditSearchParameters parameters,
            HttpContext context,
            [FromServices] SearchAuditHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<PageRequest> pageRequest = PageRequest.Create(parameters.Page, parameters.PageSize, null);

            if (pageRequest.IsFailure)
            {
                return pageRequest.ToHttpResult(context, requestContext);
            }

            Result<(IReadOnlyList<AuditEventDto> Items, long Total)> result = await handler.HandleAsync(
                new SearchAuditQuery(
                    parameters.From,
                    parameters.To,
                    parameters.Application,
                    parameters.Module,
                    parameters.Action,
                    parameters.ActorUserId,
                    parameters.ResourceType,
                    parameters.ResourceId,
                    parameters.Result,
                    pageRequest.Value.Skip,
                    pageRequest.Value.PageSize),
                cancellationToken);

            return result.IsFailure
                ? result.ToHttpResult(context, requestContext)
                : Results.Ok(new PagedResult<AuditEventDto>(
                    result.Value.Items,
                    pageRequest.Value.Page,
                    pageRequest.Value.PageSize,
                    result.Value.Total));
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.audit.view"))
            .RequireRateLimiting(RateLimitPolicies.Read)
            .WithName("SearchAuditEvents")
            .WithSummary("Searches the audit trail. The date range is required and bounded.");

        audit.MapPost("/events", async (
            IngestAuditEventDto request,
            HttpContext context,
            [FromServices] IngestAuditHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            if (request is null)
            {
                return Result.Failure(Error.Validation(
                    "AUDIT.BODY_REQUIRED", "An event is required.", "body"))
                    .ToHttpResult(context, requestContext);
            }

            return await IngestAsync(
                [request], context, handler, requestContext, cancellationToken);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.audit.write"))
            .RequireRateLimiting(RateLimitPolicies.Write)
            .WithName("IngestAuditEvent")
            .WithSummary("Records one event, attributed to the calling application.");

        audit.MapPost("/events/batch", async (
            IngestAuditEventDto[] request,
            HttpContext context,
            [FromServices] IngestAuditHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            if (request is null)
            {
                return Result.Failure(Error.Validation(
                    "AUDIT.BODY_REQUIRED", "A batch of events is required.", "body"))
                    .ToHttpResult(context, requestContext);
            }

            return await IngestAsync(request, context, handler, requestContext, cancellationToken);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.audit.write"))
            .RequireRateLimiting(RateLimitPolicies.Write)
            .WithName("IngestAuditEventBatch")
            .WithSummary("Records many events at once, attributed to the calling application.");
    }

    private static async Task<IResult> IngestAsync(
        IReadOnlyList<IngestAuditEventDto> events,
        HttpContext context,
        IngestAuditHandler handler,
        RequestContextAccessor requestContext,
        CancellationToken cancellationToken)
    {
        // The application comes from the token, never from the body. A caller
        // able to name its own application could write entries attributed to
        // Finance or HR, and a trail anyone can forge is evidence of nothing.
        string application = ResolveApplication(context);

        Result<IngestionResultDto> result = await handler.HandleAsync(
            new IngestAuditCommand(
                application,
                events,
                requestContext.IpAddress,
                requestContext.UserAgent,
                requestContext.CorrelationId),
            cancellationToken);

        return result.IsFailure
            ? result.ToHttpResult(context, requestContext)
            : Results.Accepted(value: result.Value);
    }

    /// <summary>
    /// Which application this caller writes as.
    /// <para>
    /// The <c>client_id</c> claim when a business application authenticates as
    /// itself; otherwise <c>platform</c>, because a signed-in person acting
    /// through the Platform's own API is the Platform acting. Machine-to-machine
    /// credentials arrive in Phase 11 and will populate that claim.
    /// </para>
    /// </summary>
    private static string ResolveApplication(HttpContext context)
        => context.User.FindFirst("client_id")?.Value
        ?? context.User.FindFirst("azp")?.Value
        ?? "platform";
}

/// <summary>
/// Search parameters, bound from the query string.
/// <para>
/// A parameter object rather than fourteen arguments: the endpoint signature was
/// past the point where anyone could see which value went where.
/// </para>
/// </summary>
public sealed record AuditSearchParameters
{
    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public string? Application { get; init; }

    public string? Module { get; init; }

    public string? Action { get; init; }

    public Guid? ActorUserId { get; init; }

    public string? ResourceType { get; init; }

    public string? ResourceId { get; init; }

    public string? Result { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }
}
