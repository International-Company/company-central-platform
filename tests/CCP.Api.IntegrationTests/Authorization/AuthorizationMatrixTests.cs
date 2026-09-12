using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CCP.Kernel.Api.Security;
using CCP.Modules.Identity.Domain.Credentials;
using CCP.Modules.Identity.Domain.Users;
using CCP.Modules.Identity.Infrastructure.Persistence;
using CCP.Modules.Identity.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
                    new Uri(Fill(endpoint.RoutePattern.RawText!), UriKind.Relative))
                {
                    Content = BodyFor(endpoint, method)
                };

                using HttpResponseMessage response = await client.SendAsync(request);

                if (response.StatusCode != HttpStatusCode.Unauthorized)
                {
                    failures.Add(await DescribeAsync(endpoint, method, response));
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
                new Uri(Fill(endpoint.RoutePattern.RawText!), UriKind.Relative))
            {
                Content = BodyFor(endpoint, method)
            };

            using HttpResponseMessage response = await client.SendAsync(request);

            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                failures.Add(await DescribeAsync(endpoint, method, response));
            }
        }

        Assert.True(
            failures.Count == 0,
            "These endpoints accepted a forged token:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// A real, signed-in account that holds nothing is refused by every endpoint
    /// that demands a permission.
    /// <para>
    /// The second column of the matrix, and the one that catches a different
    /// mistake from the first. Refusing an anonymous caller only proves the
    /// authentication middleware runs; this proves the <i>permission</i> is
    /// actually consulted. An endpoint that declared a permission and was mapped
    /// with a bare <c>RequireAuthorization()</c> would refuse nobody who had
    /// merely managed to sign in — which is every employee in the company.
    /// </para>
    /// <para>
    /// <b>403 and never 500.</b> A 500 here would mean the handler ran and threw
    /// before anything checked the caller, which is both a defect and a
    /// disclosure: the caller learns what the endpoint does with input it should
    /// never have reached.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ACallerWithNoPermissionsIsRefusedByEveryPermissionGatedEndpoint()
    {
        using HttpClient client = _host.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await SignInWithNothingAsync(client));

        var failures = new List<string>();
        int checkedEndpoints = 0;

        foreach (RouteEndpoint endpoint in ProtectedEndpoints())
        {
            // Only the endpoints that demand a permission. Those marked
            // authenticated-user-only are supposed to answer this caller --
            // reading one's own profile is not a privilege — and asserting 403
            // there would be asserting the opposite of the design.
            if (endpoint.Metadata.GetMetadata<RequirePermissionAttribute>() is null)
            {
                continue;
            }

            string method = MethodsOf(endpoint).First();
            checkedEndpoints++;

            using var request = new HttpRequestMessage(
                new HttpMethod(method),
                new Uri(Fill(endpoint.RoutePattern.RawText!), UriKind.Relative))
            {
                Content = BodyFor(endpoint, method)
            };

            using HttpResponseMessage response = await client.SendAsync(request);

            if (response.StatusCode != HttpStatusCode.Forbidden)
            {
                failures.Add(await DescribeAsync(endpoint, method, response));
            }
        }

        Assert.True(
            checkedEndpoints > 30,
            $"Only {checkedEndpoints} permission-gated endpoints were checked.");

        Assert.True(
            failures.Count == 0,
            "These endpoints did not refuse a signed-in caller holding no permissions. "
            + "A 2xx is an authorization hole; a 500 means the handler ran before "
            + "anything checked the caller:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Creates an account with no role at all and signs it in.
    /// <para>
    /// Deliberately not the seeded administrator, and deliberately not a
    /// purpose-built narrow role either: the subject here is a caller who holds
    /// <i>nothing</i>, which is what a new employee is on their first morning.
    /// </para>
    /// </summary>
    private async Task<string> SignInWithNothingAsync(HttpClient client)
    {
        const string Password = "correct-horse-battery-staple";

        // Version 4. The leading hex of a v7 is a millisecond timestamp, so
        // truncating one produces names that collide between tests.
        string username = $"nul{Guid.NewGuid():N}"[..20];

        await using var identity = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(factory.TestConnectionString)
                .Options);

        User user = User.Create(
            username, $"{username}@example.invalid", "No Permissions",
            DateTimeOffset.UtcNow, mustChangePassword: false).Value;

        var hasher = new Argon2PasswordHasher(Options.Create(new Argon2Options
        {
            MemoryKib = 8192,
            Iterations = 1,
            Parallelism = 1
        }));

        identity.Users.Add(user);
        identity.Credentials.Add(UserCredential.Create(
            user.Id, hasher.Hash(Password), hasher.AlgorithmId, DateTimeOffset.UtcNow));

        await identity.SaveChangesAsync();

        using HttpResponseMessage login = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { username, password = Password });

        Assert.True(login.IsSuccessStatusCode, $"Sign-in failed: {login.StatusCode}.");

        JsonElement body = await login.Content.ReadFromJsonAsync<JsonElement>();

        return body.GetProperty("accessToken").GetString()!;
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

            // The OpenAPI document is mapped by the framework and only in
            // Development, which the test host is and production is not. It is
            // excluded because it is not part of the Platform's surface, not
            // because it is uninteresting -- if it were ever mapped outside
            // Development it would publish the contract to anybody, and the
            // guard against that is the `if (app.Environment.IsDevelopment())`
            // around it rather than anything here.
            .Where(endpoint => !endpoint.RoutePattern.RawText!.StartsWith("/openapi", StringComparison.Ordinal))
            .OrderBy(endpoint => endpoint.RoutePattern.RawText, StringComparer.Ordinal);
    }

    /// <summary>
    /// A body of the kind the endpoint says it accepts.
    /// <para>
    /// <b>This began as a way to stop a false failure and became the experiment
    /// that settled a real question.</b> On the first run the two upload
    /// endpoints answered 415 to a JSON body, and where that 415 comes from
    /// decides whether the Platform has a hole: binding and the handler run
    /// <i>after</i> the authorization middleware, so a 415 produced there would
    /// mean an anonymous caller had already been let through. Sending a body of
    /// the declared kind removes that explanation and leaves only one reading of
    /// the result.
    /// </para>
    /// <para>
    /// <b>The answer became 401.</b> The refusal was correctly ordered all along
    /// and the 415 was the test's own doing — the framework rejects a
    /// mismatched content type on a form endpoint before the pipeline reaches
    /// authorization. Recorded here rather than quietly deleted, because the
    /// reasoning is the reusable part: a status other than 401 from this test is
    /// not automatically a false alarm, and which one it is depends on where in
    /// the pipeline it was produced.
    /// </para>
    /// </summary>
    private static HttpContent? BodyFor(RouteEndpoint endpoint, string method)
    {
        if (method is not ("POST" or "PUT" or "PATCH"))
        {
            return null;
        }

        IReadOnlyList<string> accepted =
            endpoint.Metadata.GetMetadata<IAcceptsMetadata>()?.ContentTypes ?? [];

        if (accepted.Any(type => type.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase)))
        {
            return new MultipartFormDataContent();
        }

        if (accepted.Any(type =>
                type.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)))
        {
            return new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>());
        }

        return new StringContent("{}", Encoding.UTF8, "application/json");
    }

    /// <summary>
    /// A failure line carrying the response body.
    /// <para>
    /// The Platform answers refusals as RFC 9457 problem details with a machine
    /// code, so the body says which check refused. A bare status number would
    /// leave the next person guessing exactly the way this test's first run left
    /// its author guessing.
    /// </para>
    /// </summary>
    private static async Task<string> DescribeAsync(
        RouteEndpoint endpoint, string method, HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();

        if (body.Length > 300)
        {
            body = body[..300];
        }

        return FormattableString.Invariant(
            $"{method} {endpoint.RoutePattern.RawText} answered {(int)response.StatusCode}: {body}");
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
        private const int Unlimited = 100_000;

        // The base class turns this into the host's RateLimits:Authentication.
        // Setting it here rather than in the loop below keeps one policy from
        // being written twice with different numbers, where the winner would be
        // whichever call happened to come last.
        protected override int AuthenticationRateLimit => Unlimited;

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
            // process-global and outlives the host that set it: raising one here
            // would raise it for every host built afterwards in this process,
            // including RateLimitTests, whose entire subject is the limiter
            // refusing. xUnit does not define the order test classes run in, so
            // that is an order-dependent failure -- a bug this project has
            // already shipped once and does not intend to ship twice.
            //
            // This comment used to say the classes ran in parallel. They do not:
            // AssemblyInfo.cs disables parallelization, for the connection
            // string's sake. The conclusion survived the correction because it
            // never depended on concurrency -- only on the order being nobody's
            // choice -- but a justification citing a mechanism the repository
            // has switched off is one somebody will eventually act on.
            string unlimited = Unlimited.ToString(CultureInfo.InvariantCulture);

            foreach (string policy in new[] { "Anonymous", "Read", "Write" })
            {
                builder.UseSetting($"RateLimits:{policy}", unlimited);
            }

            base.ConfigureWebHost(builder);
        }
    }
}
