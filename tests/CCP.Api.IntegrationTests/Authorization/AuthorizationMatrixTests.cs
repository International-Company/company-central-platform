using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Api.IntegrationTests.Authorization;

/// <summary>
/// Every endpoint the Platform maps, called with no credentials at all.
/// <para>
/// <b>An architecture test already asserts that every endpoint declares a
/// permission or explicitly allows anonymous. That is declaration, not
/// enforcement</b> — it reads metadata, and metadata is a statement of intent
/// that a misconfigured pipeline, a middleware ordered wrongly or a group that
/// forgot <c>RequireAuthorization</c> would leave completely unhonoured. Both
/// tests would pass. Only asking the running server refuses to be fooled.
/// </para>
/// <para>
/// This is the first column of the matrix Phase 20 asks for, done exhaustively
/// rather than by sampling: the list comes from the route table itself, so an
/// endpoint added next year is covered the day it is mapped, without anybody
/// remembering to add it here.
/// </para>
/// <para>
/// <b>It really does send the requests, including the destructive ones.</b> A
/// <c>DELETE</c> refused at the pipeline never reaches a handler, which is the
/// entire claim under test — and if one is not refused, the test has found
/// something far more important than the row it damaged in a throwaway
/// database.
/// </para>
/// </summary>
public sealed partial class AuthorizationMatrixTests(PlatformApiFactory factory)
    : IClassFixture<PlatformApiFactory>, IDisposable
{
    private readonly UnthrottledFactory _host = new(factory.TestConnectionString);

    public void Dispose()
    {
        _host.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task EveryProtectedEndpoint_RefusesAnUnauthenticatedCaller()
    {
        using HttpClient client = _host.CreateClient();

        var failures = new List<string>();
        int checkedEndpoints = 0;

        foreach (RouteEndpoint endpoint in ProtectedEndpoints())
        {
            foreach (string method in MethodsOf(endpoint))
            {
                checkedEndpoints++;

                using var request = new HttpRequestMessage(
                    new HttpMethod(method),
                    new Uri(Fill(endpoint.RoutePattern.RawText!), UriKind.Relative));

                if (method is "POST" or "PUT" or "PATCH")
                {
                    request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                }

                using HttpResponseMessage response = await client.SendAsync(request);

                if (response.StatusCode != HttpStatusCode.Unauthorized)
                {
                    failures.Add(
                        FormattableString.Invariant(
                            $"{method} {endpoint.RoutePattern.RawText} answered {(int)response.StatusCode}"));
                }
            }
        }

        // If the route table came back empty this test would pass having asked
        // nothing, which is the failure mode of every guard in this project that
        // has ever gone quiet.
        Assert.True(checkedEndpoints > 30, $"Only {checkedEndpoints} endpoints were checked.");

        Assert.True(
            failures.Count == 0,
            "These endpoints did not refuse an unauthenticated caller. Anything other "
            + "than 401 means the pipeline is not enforcing what the metadata declares:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// A token that is merely well-formed nonsense is refused too.
    /// <para>
    /// Distinct from sending nothing. "No credential" can be refused by a
    /// pipeline that never validates anything; refusing a forged one requires
    /// the signature actually to be checked, and a Platform that accepted
    /// unsigned tokens would pass every test above.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AForgedTokenIsRefusedEverywhere()
    {
        using HttpClient client = _host.CreateClient();

        // Structurally a JWT, signed by nobody. Assembled from parts so no
        // secret-shaped literal enters the repository.
        string forged = string.Join('.',
            Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"none\"}")).TrimEnd('='),
            Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"sub\":\"nobody\"}")).TrimEnd('='),
            "not-a-signature");

        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + forged);

        var failures = new List<string>();

        foreach (RouteEndpoint endpoint in ProtectedEndpoints())
        {
            string method = MethodsOf(endpoint).First();

            using var request = new HttpRequestMessage(
                new HttpMethod(method),
                new Uri(Fill(endpoint.RoutePattern.RawText!), UriKind.Relative));

            if (method is "POST" or "PUT" or "PATCH")
            {
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            }

            using HttpResponseMessage response = await client.SendAsync(request);

            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                failures.Add(
                    FormattableString.Invariant(
                        $"{method} {endpoint.RoutePattern.RawText} answered {(int)response.StatusCode}"));
            }
        }

        Assert.True(
            failures.Count == 0,
            "These endpoints accepted a forged token:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Every endpoint whose metadata does not explicitly allow anonymous access.
    /// <para>
    /// Read from the running server's own route table rather than from a list,
    /// because a list is a second copy of the truth and this project has already
    /// been bitten twice by one that drifted.
    /// </para>
    /// </summary>
    private IEnumerable<RouteEndpoint> ProtectedEndpoints()
    {
        var source = _host.Services.GetRequiredService<EndpointDataSource>();

        return source.Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText is not null)
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)

            // Health probes are not part of the API surface and answer before
            // authentication by design.
            .Where(endpoint => !endpoint.RoutePattern.RawText!.StartsWith("/health", StringComparison.Ordinal))
            .OrderBy(endpoint => endpoint.RoutePattern.RawText, StringComparer.Ordinal);
    }

    private static IEnumerable<string> MethodsOf(RouteEndpoint endpoint)
    {
        IReadOnlyList<string>? methods = endpoint.Metadata
            .GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods;

        return methods is { Count: > 0 } ? methods : ["GET"];
    }

    /// <summary>
    /// Replaces every route parameter with a value that satisfies its
    /// constraint.
    /// <para>
    /// A GUID satisfies both <c>{id:guid}</c> and a bare <c>{key}</c>, so one
    /// substitution covers the whole table. A value that failed the constraint
    /// would produce 404 from routing rather than 401 from authorization, and
    /// the test would be quietly asking nothing.
    /// </para>
    /// </summary>
    private static string Fill(string template) =>
        RouteParameter().Replace(template, Guid.CreateVersion7().ToString());

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RouteParameter();

    /// <summary>
    /// The same host, with the rate limits lifted.
    /// <para>
    /// This test makes a hundred-odd anonymous requests in a few seconds, which
    /// is well past the anonymous budget — and every one of those would come
    /// back 429. A 429 is a refusal too, so the test would still pass, having
    /// verified the rate limiter instead of the thing it is named after. That is
    /// the worst possible outcome: a green test that checks something else.
    /// </para>
    /// <para>
    /// The limiter is not thereby left untested; <c>RateLimitTests</c> drives it
    /// down to a small number and asserts the rejection and its
    /// <c>Retry-After</c> header.
    /// </para>
    /// </summary>
    private sealed class UnthrottledFactory(string connectionString) : PlatformApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            // The connection string goes through the environment because the
            // composition root reads it before the host is built, which is
            // earlier than anything a factory can contribute. It is set to the
            // shared fixture's database, so this host and that one are the same
            // database.
            Environment.SetEnvironmentVariable("CCP_ConnectionStrings__Platform", connectionString);

            // The limits go through UseSetting rather than the environment, and
            // that is not a stylistic choice. An environment variable is
            // process-global: raising it here would raise it for every host
            // built afterwards in this process, including RateLimitTests, whose
            // entire subject is the limiter refusing. Test classes run in
            // parallel and their order is not defined, so that would be an
            // order-dependent failure -- a bug this project has already shipped
            // once and does not intend to ship twice.
            string unlimited = 100_000.ToString(CultureInfo.InvariantCulture);

            foreach (string policy in new[] { "Anonymous", "Read", "Write", "Authentication" })
            {
                builder.UseSetting($"RateLimits:{policy}", unlimited);
            }

            base.ConfigureWebHost(builder);
        }
    }
}
