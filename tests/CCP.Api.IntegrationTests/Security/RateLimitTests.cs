using System.Net;
using System.Net.Http.Json;

namespace CCP.Api.IntegrationTests.Security;

/// <summary>
/// A factory whose authentication budget is small enough to exhaust on purpose.
/// </summary>
public sealed class RateLimitedApiFactory : PlatformApiFactory
{
    /// <summary>
    /// Three a minute. Small enough that a handful of requests crosses it, large
    /// enough that the first request or two are demonstrably allowed — a limit
    /// of one could not tell "the limiter works" from "the endpoint is broken".
    /// </summary>
    public const int Limit = 3;

    protected override int AuthenticationRateLimit => Limit;
}

/// <summary>
/// That the limiter actually rejects, and says when to come back.
/// <para>
/// The rest of the integration suite runs with the budget raised, because every
/// test shares one address and would otherwise spend the whole allowance in the
/// first ten sign-ins. That is a reasonable accommodation only if something
/// still proves the limiter works — which is what this class is for.
/// </para>
/// </summary>
public sealed class RateLimitTests(RateLimitedApiFactory factory) : IClassFixture<RateLimitedApiFactory>
{
    [Fact]
    public async Task SignIn_IsRejected_OnceTheBudgetIsSpent()
    {
        using HttpClient client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();

        // One account throughout. The budget is partitioned by the account
        // being attacked, so varying the username would hand each attempt a
        // fresh budget and prove nothing — which is the whole point of the
        // change, and exactly what NatRateLimitTests checks from the other side.
        //
        // Credentials are wrong every time: the limiter must not depend on
        // whether an attempt would have succeeded, or an attacker learns which
        // accounts exist by watching where the limit bites.
        for (int i = 0; i < RateLimitedApiFactory.Limit + 3; i++)
        {
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("/api/v1/auth/login", UriKind.Relative),
                new { username = "the-targeted-account", password = $"wrong-password-{i}" });

            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);

        // The early attempts must have been served. If every request were
        // rejected the test would pass while proving the endpoint unreachable
        // rather than the limiter effective.
        Assert.Contains(statuses, s => s != HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Rejection_CarriesRetryAfter()
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage? rejected = null;

        for (int i = 0; i < RateLimitedApiFactory.Limit + 5 && rejected is null; i++)
        {
            HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("/api/v1/auth/login", UriKind.Relative),
                new { username = "the-retry-after-account", password = $"wrong-password-{i}" });

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected = response;
            }
            else
            {
                response.Dispose();
            }
        }

        Assert.NotNull(rejected);

        try
        {
            // Tells an honest client to back off instead of hammering. An
            // attacker ignores it, which costs nothing.
            Assert.NotNull(rejected.Headers.RetryAfter);
        }
        finally
        {
            rejected.Dispose();
        }
    }
}
