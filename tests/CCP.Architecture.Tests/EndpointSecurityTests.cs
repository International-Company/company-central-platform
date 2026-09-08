using CCP.Kernel.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CCP.Architecture.Tests;

/// <summary>
/// Every endpoint must declare its access rule explicitly: either a required
/// permission, or an explicit decision to allow anonymous access.
/// <para>
/// The failure this prevents is an endpoint that is unprotected because nobody
/// remembered to protect it — the most common way a real system leaks data.
/// Silence is not a valid access policy (P6, P9).
/// </para>
/// <para>
/// Phase 1 has only anonymous diagnostics endpoints, so today the test mostly
/// proves the mechanism works. It becomes load-bearing in Phase 4, when the
/// Authorization module arrives and real endpoints must carry permissions.
/// </para>
/// </summary>
public sealed class EndpointSecurityTests
{
    [Fact]
    public async Task EveryEndpoint_DeclaresPermissionOrExplicitlyAllowsAnonymous()
    {
        IReadOnlyList<RouteEndpoint> endpoints = await GetApiEndpointsAsync();

        Assert.NotEmpty(endpoints);

        var undeclared = new List<string>();

        foreach (RouteEndpoint endpoint in endpoints)
        {
            bool hasPermission = endpoint.Metadata.GetMetadata<RequirePermissionAttribute>() is not null;
            bool allowsAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            bool authenticatedOnly = endpoint.Metadata.GetMetadata<AuthenticatedUserOnlyAttribute>() is not null;

            if (!hasPermission && !allowsAnonymous && !authenticatedOnly)
            {
                undeclared.Add($"{FormatMethods(endpoint)} /{endpoint.RoutePattern.RawText?.TrimStart('/')}");
            }
        }

        Assert.True(
            undeclared.Count == 0,
            "Every API endpoint must carry [RequirePermission] or explicitly AllowAnonymous "
            + "(ARCHITECTURE.md §26.1). These declare neither:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, undeclared));
    }

    [Fact]
    public async Task AnonymousEndpoints_AreLimitedToTheKnownPublicSurface()
    {
        IReadOnlyList<RouteEndpoint> endpoints = await GetApiEndpointsAsync();

        // Anonymous access is a deliberate exception, so the permitted set is
        // listed here. Adding one means editing this test, which forces the
        // decision to be reviewed rather than made silently in passing.
        string[] knownAnonymousPrefixes =
        [
            "api/v1/diagnostics",

            // Sign-in and refresh must be reachable before a caller holds any
            // credential. Sign-out is not here: it requires authentication.
            "api/v1/auth/login",
            "api/v1/auth/refresh",

            // Password recovery: someone who has forgotten their password
            // cannot authenticate, so these cannot require it. Both are
            // enumeration-safe, and both need strict per-IP rate limits in
            // Phase 5 — they are the most exposed surface in the Platform.
            "api/v1/auth/password/forgot",
            "api/v1/auth/password/reset",

            // Public keys, by definition. A business application fetches these
            // before it holds any credential, and there is nothing here to
            // protect — the private key cannot reach this document.
            "api/v1/.well-known/jwks.json"
        ];

        var unexpected = new List<string>();

        foreach (RouteEndpoint endpoint in endpoints)
        {
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            {
                continue;
            }

            string route = endpoint.RoutePattern.RawText?.TrimStart('/') ?? string.Empty;

            if (!knownAnonymousPrefixes.Any(p => route.StartsWith(p, StringComparison.Ordinal)))
            {
                unexpected.Add(route);
            }
        }

        Assert.True(
            unexpected.Count == 0,
            "These endpoints allow anonymous access but are not in the reviewed public surface. "
            + "If the access is intended, add the prefix to this test so the decision is recorded:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, unexpected));
    }

    /// <summary>
    /// Boots the real host and reads the endpoints it actually registered, so
    /// the test reflects what ships rather than a reimplementation of it.
    /// </summary>
    private static async Task<IReadOnlyList<RouteEndpoint>> GetApiEndpointsAsync()
    {
        using IHost host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseStartup<ArchitectureTestStartup>();
            })
            .StartAsync();

        var dataSource = host.Services.GetRequiredService<EndpointDataSource>();

        return
        [
            .. dataSource.Endpoints
                .OfType<RouteEndpoint>()
                .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
        ];
    }

    private static string FormatMethods(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();

        return methods is null ? "ANY" : string.Join("|", methods.HttpMethods);
    }
}

/// <summary>
/// Minimal startup that maps the real module endpoints without requiring the
/// database, secret manager or other runtime dependencies the full host needs.
/// </summary>
internal sealed class ArchitectureTestStartup
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddRouting();
        services.AddAuthorization();
    }

    public static void Configure(IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            RouteGroupBuilder v1 = endpoints.MapGroup("/api/v1");
            CCP.Api.Host.Modules.PlatformModules.MapPlatformModules(v1);
        });
    }
}
