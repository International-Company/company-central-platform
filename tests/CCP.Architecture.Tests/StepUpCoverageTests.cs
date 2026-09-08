using CCP.Kernel.Api.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CCP.Architecture.Tests;

/// <summary>
/// Which endpoints demand a recent second factor, and which do not.
/// <para>
/// Step-up is how the Platform enforces MFA for privileged work
/// (ARCHITECTURE.md §12.5). Where it applies is a security decision, so the set
/// is pinned here in both directions: an endpoint that quietly loses its step-up
/// requirement fails this test, and so does one that gains an unreviewed one.
/// Reviewing the change is the point — a test that only checked a minimum would
/// let elevation spread until people worked around it.
/// </para>
/// </summary>
public sealed class StepUpCoverageTests
{
    /// <summary>
    /// The reviewed set. Everything here creates, restores or removes someone's
    /// access — the actions where a stolen session does the most damage and
    /// where demanding the factor again costs an honest administrator fifteen
    /// seconds.
    /// </summary>
    private static readonly string[] ExpectedStepUpRoutes =
    [
        "POST api/v1/users",                       // a new account is a new way in
        "POST api/v1/users/{id:guid}/enable",      // restores a way in that was closed
        "POST api/v1/users/{id:guid}/disable",     // removes the person who would notice
        "POST api/v1/users/{id:guid}/unlock",      // undoes a lockout that was working
        "POST api/v1/users/{id:guid}/roles",       // how access is created
        "DELETE api/v1/users/{id:guid}/roles/{assignmentId:guid}"  // how access is destroyed
    ];

    [Fact]
    public async Task StepUpProtectedEndpoints_AreExactlyTheReviewedSet()
    {
        IReadOnlyList<RouteEndpoint> endpoints = await GetApiEndpointsAsync();

        Assert.NotEmpty(endpoints);

        List<string> actual =
        [
            .. endpoints
                .Where(e => e.Metadata.GetMetadata<RequireStepUpAttribute>() is not null)
                .Select(Describe)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
        ];

        string[] expected = [.. ExpectedStepUpRoutes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        string[] missing = [.. expected.Except(actual, StringComparer.Ordinal)];
        string[] unexpected = [.. actual.Except(expected, StringComparer.Ordinal)];

        Assert.True(
            missing.Length == 0,
            "These endpoints are meant to require a recent second factor and no longer do:"
            + Environment.NewLine + string.Join(Environment.NewLine, missing));

        Assert.True(
            unexpected.Length == 0,
            "These endpoints require step-up but are not in the reviewed set. If that is "
            + "intended, add them to this test so the decision is recorded:"
            + Environment.NewLine + string.Join(Environment.NewLine, unexpected));
    }

    [Fact]
    public async Task StepUpNeverStandsAlone()
    {
        IReadOnlyList<RouteEndpoint> endpoints = await GetApiEndpointsAsync();

        List<string> withoutPermission =
        [
            .. endpoints
                .Where(e => e.Metadata.GetMetadata<RequireStepUpAttribute>() is not null
                         && e.Metadata.GetMetadata<RequirePermissionAttribute>() is null)
                .Select(Describe)
        ];

        // Step-up answers "are they still here"; it does not answer "may they do
        // this at all". An endpoint protected only by step-up would admit any
        // user who had proven a code — which is every enrolled employee.
        Assert.True(
            withoutPermission.Count == 0,
            "[RequireStepUp] must accompany [RequirePermission], never replace it. These carry "
            + "step-up with no permission:"
            + Environment.NewLine + string.Join(Environment.NewLine, withoutPermission));
    }

    [Fact]
    public async Task MfaEndpoints_DoNotRequireStepUp()
    {
        IReadOnlyList<RouteEndpoint> endpoints = await GetApiEndpointsAsync();

        List<string> circular =
        [
            .. endpoints
                .Where(e => e.RoutePattern.RawText?.Contains("/mfa", StringComparison.Ordinal) == true
                         && e.Metadata.GetMetadata<RequireStepUpAttribute>() is not null)
                .Select(Describe)
        ];

        // The deadlock this prevents: needing elevation to enrol the factor that
        // grants elevation. Nobody could ever enrol.
        Assert.True(
            circular.Count == 0,
            "MFA endpoints must not require step-up — enrolling the factor cannot depend on "
            + "already holding it:"
            + Environment.NewLine + string.Join(Environment.NewLine, circular));
    }

    /// <summary>
    /// "POST api/v1/users". The method matters: <c>GET /users</c> and
    /// <c>POST /users</c> are the same route and must not share a verdict — one
    /// lists people, the other creates them.
    /// </summary>
    private static string Describe(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();

        // Route groups produce a trailing slash for a "/" pattern, which is the
        // same endpoint under a different spelling.
        string route = (endpoint.RoutePattern.RawText ?? string.Empty).Trim('/');

        return methods is null
            ? $"ANY {route}"
            : $"{string.Join("|", methods.HttpMethods)} {route}";
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
