using CCP.Kernel.Api.Context;
using CCP.Kernel.Api.Errors;
using CCP.Kernel.Api.Modules;
using CCP.Kernel.Api.Security;
using CCP.Kernel.Application.Modules;
using CCP.Kernel.Paging;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Api.Host.Modules.Diagnostics;

/// <summary>
/// The Phase 1 vertical slice.
/// <para>
/// This is not a capability module and owns no data. It exists to prove that
/// the whole pipeline works end to end — module registration, endpoint mapping,
/// correlation ids, the error contract, paging validation and the response
/// envelope — before any real module is built on top of it.
/// </para>
/// <para>
/// It is removed, or reduced to the ping endpoint alone, once the Monitoring
/// module arrives in Phase 14.
/// </para>
/// </summary>
public sealed class DiagnosticsModule : IPlatformModule, IModuleEndpoints
{
    public string Name => "diagnostics";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // No services yet. The method exists so the contract is exercised.
    }

    // Service parameters are marked [FromServices] explicitly. Minimal APIs
    // can infer them, but inference depends on what happens to be registered
    // at mapping time, which makes endpoint mapping fail in surprising ways
    // under a different composition. Explicit is stable (P9).
    public void MapEndpoints(IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder group = versionGroup
            .MapGroup("/diagnostics")
            .WithTags("Diagnostics");

        // Anonymous by design: this is the liveness probe for the API surface
        // and must work before a caller has any credentials.
        group.MapGet("/ping", (
            [FromServices] RequestContextAccessor requestContext,
            [FromServices] IClock clock) =>
            Microsoft.AspNetCore.Http.Results.Ok(new PingResponse(
                Status: "ok",
                ServerTimeUtc: clock.UtcNow,
                CorrelationId: requestContext.CorrelationId)))
            .AllowAnonymous()
            .WithName("Ping")
            .WithSummary("Confirms the API is reachable and returns the correlation id.");

        // Demonstrates that the RFC 9457 error contract is produced correctly
        // for each failure category, and lets the integration tests assert the
        // exact response shape without needing a real module.
        group.MapGet("/error/{kind}", (
            string kind,
            HttpContext context,
            [FromServices] RequestContextAccessor requestContext) =>
        {
            Result result = kind.ToLowerInvariant() switch
            {
                "validation" => Result.Failure(
                [
                    Error.Validation("PLATFORM.FIELD_REQUIRED", "Name is required.", "name"),
                    Error.Validation("PLATFORM.FIELD_TOO_LONG", "Code is too long.", "code")
                ]),
                "notfound" => Result.Failure(
                    Error.NotFound("PLATFORM.RESOURCE_NOT_FOUND", "The requested resource does not exist.")),
                "conflict" => Result.Failure(
                    Error.Conflict("PLATFORM.RESOURCE_CONFLICT", "The resource is in a conflicting state.")),
                "forbidden" => Result.Failure(
                    Error.Forbidden("PLATFORM.PERMISSION_DENIED", "Permission denied.")),
                _ => Result.Failure(
                    Error.Rule("PLATFORM.UNKNOWN_ERROR_KIND", $"Unknown error kind '{kind}'."))
            };

            return result.ToHttpResult(context, requestContext);
        })
            .AllowAnonymous()
            .WithName("ErrorContract")
            .WithSummary("Returns each error category so the contract can be verified.");

        // Demonstrates that paging is validated centrally: an out-of-range page
        // size is rejected by the kernel, not by each endpoint remembering to.
        group.MapGet("/paged", (
            int? page,
            int? pageSize,
            string? sort,
            HttpContext context,
            [FromServices] RequestContextAccessor requestContext) =>
        {
            Result<PageRequest> pageRequest = PageRequest.Create(page, pageSize, sort);

            if (pageRequest.IsFailure)
            {
                return pageRequest.ToHttpResult(context, requestContext);
            }

            Result<SortSpec?> sortSpec = SortSpec.Parse(
                pageRequest.Value.Sort,
                new HashSet<string>(StringComparer.Ordinal) { "name", "createdAt" });

            if (sortSpec.IsFailure)
            {
                return sortSpec.ToHttpResult(context, requestContext);
            }

            var items = Enumerable
                .Range(pageRequest.Value.Skip + 1, pageRequest.Value.PageSize)
                .Select(i => new SampleItem(i, $"Item {i}"))
                .ToArray();

            var result = new PagedResult<SampleItem>(
                items,
                pageRequest.Value.Page,
                pageRequest.Value.PageSize,
                totalItems: 137);

            return Microsoft.AspNetCore.Http.Results.Ok(result);
        })
            .AllowAnonymous()
            .WithName("PagedSample")
            .WithSummary("Returns a paged envelope so paging and sorting rules can be verified.");

        // Proves an unhandled exception never reaches the caller as a stack
        // trace. The integration tests assert the response body reveals nothing.
        group.MapGet("/throw", IResult () =>
            throw new InvalidOperationException(
                "Deliberate failure raised by the diagnostics endpoint."))
            .AllowAnonymous()
            .WithName("ThrowSample")
            .WithSummary("Raises an unhandled exception to verify the error boundary.");
    }
}

/// <summary>Response of the ping endpoint.</summary>
public sealed record PingResponse(string Status, DateTimeOffset ServerTimeUtc, string CorrelationId);

/// <summary>A placeholder row used only by the paging sample.</summary>
public sealed record SampleItem(int Id, string Name);
