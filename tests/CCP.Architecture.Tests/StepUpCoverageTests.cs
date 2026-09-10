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
        "DELETE api/v1/users/{id:guid}/roles/{assignmentId:guid}",  // how access is destroyed

        // Changing what a role carries changes what everyone already holding it
        // can do, without touching a single assignment — so it reaches further
        // than any one grant while looking like an edit. Creating and renaming
        // a role are not here: an empty role grants nothing, and a name is not
        // access.
        "PUT api/v1/roles/{id:guid}/permissions",

        // Registering an application hands out a permission namespace and the
        // ability to hold roles: the machine equivalent of creating an
        // administrator, and reviewed as one.
        "POST api/v1/applications",

        // A client secret acts with no person present, for as long as nobody
        // revokes it. It is a way in that does not sleep.
        "POST api/v1/applications/{id:guid}/credentials",

        // Granting a role to a machine gives it to something that never
        // notices it has been compromised.
        "POST api/v1/applications/{id:guid}/roles",

        // Registering an integration provider decides where the Platform may
        // send data and which credential travels with it. Configuring one
        // afterwards is not here: the host is fixed at registration, and
        // everything a configuration edit can change is bounded by the
        // allow-list either way.
        "POST api/v1/integrations/providers",

        // Clearing somebody else's second factor is the single most dangerous
        // action the Platform offers: it strips the protection from an account,
        // which is the first thing an attacker does after taking one. Demanding
        // recent proof of the administrator's *own* factor means a stolen
        // session cannot be used to disarm everybody else, and it means the
        // person removing a factor has one.
        "POST api/v1/security/users/{userId:guid}/mfa/reset"
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

        // Endpoints acting on the caller's *own* factor. Matched on `/me/mfa`
        // rather than on `/mfa` anywhere, and the distinction is the whole rule
        // rather than a loosening of it: the deadlock is needing elevation to
        // enrol the factor that grants elevation, and that can only happen when
        // the endpoint acts on the caller's own enrolment. An administrator
        // clearing somebody else's factor has their own to prove, so there is
        // nothing circular about asking them to.
        List<string> circular =
        [
            .. endpoints
                .Where(e => e.RoutePattern.RawText?.Contains("/me/mfa", StringComparison.Ordinal) == true
                         && e.Metadata.GetMetadata<RequireStepUpAttribute>() is not null)
                .Select(Describe)
        ];

        // The deadlock this prevents: needing elevation to enrol the factor that
        // grants elevation. Nobody could ever enrol.
        Assert.True(
            circular.Count == 0,
            "Self-service MFA endpoints must not require step-up — enrolling the factor "
            + "cannot depend on already holding it:"
            + Environment.NewLine + string.Join(Environment.NewLine, circular));
    }

    /// <summary>
    /// The converse, so narrowing the rule above strengthens it rather than
    /// loosening it.
    /// <para>
    /// An MFA endpoint that acts on somebody <i>else</i> is the most dangerous
    /// kind there is: it removes the protection from an account, which is the
    /// first thing an attacker does after taking one. Those must carry both a
    /// permission and step-up, and there must be no way to add one that carries
    /// neither.
    /// </para>
    /// </summary>
    [Fact]
    public async Task MfaEndpointsActingOnSomebodyElse_RequireBothAPermissionAndStepUp()
    {
        IReadOnlyList<RouteEndpoint> endpoints = await GetApiEndpointsAsync();

        List<string> unguarded =
        [
            .. endpoints
                .Where(e => e.RoutePattern.RawText is { } route
                         && route.Contains("/mfa", StringComparison.Ordinal)
                         && !route.Contains("/me/mfa", StringComparison.Ordinal))
                .Where(e => e.Metadata.GetMetadata<RequireStepUpAttribute>() is null
                         || e.Metadata.GetMetadata<RequirePermissionAttribute>() is null)
                .Select(Describe)
        ];

        Assert.True(
            unguarded.Count == 0,
            "An MFA endpoint acting on somebody else must require both a permission and "
            + "step-up. These do not:"
            + Environment.NewLine + string.Join(Environment.NewLine, unguarded));
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
