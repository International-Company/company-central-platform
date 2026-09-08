using CCP.Kernel.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CCP.Architecture.Tests;

/// <summary>
/// Every credential-handling endpoint must declare a rate-limit policy.
/// <para>
/// The failure this prevents is a new authentication or password endpoint added
/// six months from now with no throttle — protected by the global backstop,
/// which is generous enough for browsing and therefore useless against
/// guessing. Nobody would notice until it was being abused.
/// </para>
/// </summary>
public sealed class RateLimitingTests
{
    /// <summary>
    /// Route prefixes that handle or verify a credential. Each must carry the
    /// strict authentication policy.
    /// </summary>
    private static readonly string[] CredentialRoutes =
    [
        "api/v1/auth/login",
        "api/v1/auth/refresh",
        "api/v1/auth/password/forgot",
        "api/v1/auth/password/reset",
        "api/v1/auth/password/change"
    ];

    [Fact]
    public async Task EveryCredentialEndpoint_CarriesTheStrictAuthenticationPolicy()
    {
        IReadOnlyList<RouteEndpoint> endpoints = await GetApiEndpointsAsync();

        var unthrottled = new List<string>();

        foreach (RouteEndpoint endpoint in endpoints)
        {
            string route = endpoint.RoutePattern.RawText?.TrimStart('/') ?? string.Empty;

            if (!CredentialRoutes.Any(r => route.StartsWith(r, StringComparison.Ordinal)))
            {
                continue;
            }

            var policy = endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>();

            if (policy?.PolicyName != RateLimitPolicies.Authentication)
            {
                unthrottled.Add($"{route} (policy: {policy?.PolicyName ?? "none"})");
            }
        }

        Assert.True(
            unthrottled.Count == 0,
            "These endpoints handle credentials but do not carry the strict authentication "
            + "rate limit (ARCHITECTURE.md §12.6). The global backstop is generous enough for "
            + "browsing and useless against guessing:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, unthrottled));
    }

    [Fact]
    public async Task EveryAnonymousEndpoint_DeclaresARateLimitPolicy()
    {
        // Anonymous endpoints are the reachable surface. Leaving one on the
        // global backstop means an unauthenticated caller gets a browsing-sized
        // budget against it.
        IReadOnlyList<RouteEndpoint> endpoints = await GetApiEndpointsAsync();

        // Diagnostics is the deliberate exception: it exists to prove the
        // pipeline works and is removed when Monitoring arrives in Phase 14.
        const string diagnosticsPrefix = "api/v1/diagnostics";

        var undeclared = new List<string>();

        foreach (RouteEndpoint endpoint in endpoints)
        {
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            {
                continue;
            }

            string route = endpoint.RoutePattern.RawText?.TrimStart('/') ?? string.Empty;

            if (route.StartsWith(diagnosticsPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>() is null)
            {
                undeclared.Add(route);
            }
        }

        Assert.True(
            undeclared.Count == 0,
            "These endpoints allow anonymous access but declare no rate-limit policy:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, undeclared));
    }

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
}
