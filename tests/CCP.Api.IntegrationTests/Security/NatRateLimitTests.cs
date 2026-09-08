using System.Net;
using System.Net.Http.Json;

namespace CCP.Api.IntegrationTests.Security;

/// <summary>
/// That colleagues behind one address do not share one sign-in budget.
/// <para>
/// This is the defect the suite itself surfaced: a test process behind one
/// address is an accurate simulation of an office behind one NAT, and the
/// original per-address limit refused everything after the tenth attempt. The
/// limit is now partitioned by the account being targeted, so these tests
/// reproduce the office and expect it to work.
/// </para>
/// </summary>
public sealed class NatRateLimitTests(RateLimitedApiFactory factory) : IClassFixture<RateLimitedApiFactory>
{
    /// <summary>
    /// Distinct people, one address. Comfortably more than the per-account
    /// budget of three, so a per-address limit would refuse most of them.
    /// </summary>
    private const int Colleagues = 12;

    [Fact]
    public async Task ManyDistinctAccounts_FromOneAddress_AreNotRefused()
    {
        using HttpClient client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();

        for (int i = 0; i < Colleagues; i++)
        {
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("/api/v1/auth/login", UriKind.Relative),
                new { username = $"colleague-{i}", password = "whatever-it-is-wrong" });

            statuses.Add(response.StatusCode);
        }

        // Each name has its own budget, so nobody is turned away for being the
        // eleventh person to arrive. They all fail authentication, of course —
        // no such accounts exist — and that is a 401, not a 429.
        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task RepeatedAttempts_AgainstOneAccount_AreStillRefused()
    {
        using HttpClient client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();

        for (int i = 0; i < RateLimitedApiFactory.Limit + 3; i++)
        {
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("/api/v1/auth/login", UriKind.Relative),
                new { username = "one-unlucky-account", password = $"guess-{i}" });

            statuses.Add(response.StatusCode);
        }

        // The half that matters. Loosening the office must not loosen the
        // guard on any individual account, and this budget now holds however
        // many addresses an attacker spreads across.
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task TheAccountBudget_IsCaseInsensitive()
    {
        using HttpClient client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();

        for (int i = 0; i < RateLimitedApiFactory.Limit + 3; i++)
        {
            // Alternating spellings of one account. If the key were taken
            // verbatim, an attacker would receive a fresh budget per spelling —
            // and there are a great many spellings.
            string username = i % 2 == 0 ? "Mixed.Case" : "mixed.case";

            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("/api/v1/auth/login", UriKind.Relative),
                new { username, password = $"guess-{i}" });

            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task AMalformedBody_StillReachesValidation()
    {
        using HttpClient client = factory.CreateClient();

        using var content = new StringContent(
            "{ this is not json", System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await client.PostAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative), content);

        // The middleware buffers and parses the body to find the account. A
        // parse failure there must not swallow the request or produce a 500 —
        // validation refuses it afterwards with a proper message.
        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest
                or HttpStatusCode.UnprocessableEntity
                or HttpStatusCode.TooManyRequests,
            $"Expected a client error, got {(int)response.StatusCode}.");
    }

    [Fact]
    public async Task TheBodyIsStillReadableByTheEndpoint()
    {
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { username = "reader-probe", password = "some-password" });

        // The regression this guards: the middleware consumes the request
        // stream to read the username. Without EnableBuffering the model binder
        // would find nothing left and every sign-in would fail as malformed.
        Assert.NotEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }
}
