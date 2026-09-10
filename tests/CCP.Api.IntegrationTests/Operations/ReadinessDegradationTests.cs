using System.Net;
using CCP.Modules.Documents.Application.Abstractions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CCP.Api.IntegrationTests.Operations;

/// <summary>
/// What readiness does when a dependency is gone.
/// <para>
/// <b>Failure injection, and the failure being injected is one this project has
/// already caused.</b> In Phase 10 the Platform refused to start because it
/// could not create a document folder — a singleton resolved while the host was
/// being built, so <i>every</i> module failed over a directory that only
/// Documents needed. The lesson written down afterwards was that a document
/// store being unreachable must stop documents and nothing else.
/// </para>
/// <para>
/// Phase 14 encoded that as <c>failureStatus: Degraded</c> on the storage check
/// and <c>Unhealthy</c> on the database. It has never been exercised. The
/// difference is not cosmetic: a load balancer reads the status code, and a
/// storage check that reported unhealthy would take every instance out of
/// rotation over a bucket — reproducing the Phase 10 outage through a different
/// door.
/// </para>
/// </summary>
public sealed class ReadinessDegradationTests(PlatformApiFactory factory)
    : IClassFixture<PlatformApiFactory>
{
    /// <summary>
    /// With storage unreachable, readiness still says yes.
    /// <para>
    /// 200, because the default mapping sends Degraded to 200 and Unhealthy to
    /// 503, and the whole point of choosing Degraded is that the instance keeps
    /// serving the ninety per cent of the Platform that has nothing to do with
    /// documents.
    /// </para>
    /// </summary>
    [Fact]
    public async Task StorageBeingUnreachableDegradesRatherThanStopsThePlatform()
    {
        using WebApplicationFactory<Program> host = WithBrokenStorage();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();

        // The bare status word, which is all readiness publishes. Anything more
        // would name internals to whoever can reach the probe.
        Assert.Equal("Degraded", body.Trim());
    }

    /// <summary>
    /// And the rest of the Platform still answers.
    /// <para>
    /// The status code above proves what the load balancer sees. This proves
    /// what a caller sees, which is the claim that actually matters: a 401 from
    /// an authorization-protected endpoint means the pipeline is running
    /// normally with the bucket gone.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheRestOfThePlatformStillServesWithStorageGone()
    {
        using WebApplicationFactory<Program> host = WithBrokenStorage();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/users", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Liveness is unmoved. It answers whether the process is running, and the
    /// process is running — a liveness probe that failed here would have the
    /// container platform restart a perfectly healthy instance, repeatedly,
    /// because a bucket was down.
    /// </summary>
    [Fact]
    public async Task LivenessIsUnaffected()
    {
        using WebApplicationFactory<Program> host = WithBrokenStorage();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// With storage working, readiness is fully healthy — so the test above is
    /// showing the injected failure rather than a Platform that is degraded all
    /// the time.
    /// </summary>
    [Fact]
    public async Task ReadinessIsHealthyWhenNothingIsBroken()
    {
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", (await response.Content.ReadAsStringAsync()).Trim());
    }

    private WebApplicationFactory<Program> WithBrokenStorage() =>
        factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Replace(
                    ServiceDescriptor.Singleton<IDocumentStorageProvider, UnreachableStorage>())));

    /// <summary>
    /// A store that is there and cannot be reached.
    /// <para>
    /// It throws rather than returning null, because null is the
    /// <i>healthy</i> answer to the probe — the store was reached and said the
    /// object is not there. A stub that returned null would report perfect
    /// health and this suite would prove nothing.
    /// </para>
    /// </summary>
    private sealed class UnreachableStorage : IDocumentStorageProvider
    {
        public string Name => "unreachable";

        public Task StoreAsync(
            string objectKey, Stream content, string contentType,
            CancellationToken cancellationToken = default)
            => throw new IOException("the bucket is unreachable");

        public Task<Stream?> OpenAsync(string objectKey, CancellationToken cancellationToken = default)
            => throw new IOException("the bucket is unreachable");

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
            => throw new IOException("the bucket is unreachable");

        public Task<Uri?> TryCreateReadUrlAsync(
            string objectKey, string fileName, string contentType, TimeSpan lifetime,
            CancellationToken cancellationToken = default)
            => throw new IOException("the bucket is unreachable");
    }
}
