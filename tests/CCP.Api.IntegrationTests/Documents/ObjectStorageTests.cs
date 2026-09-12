using System.Net;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using CCP.Modules.Documents.Application.Abstractions;
using CCP.Modules.Documents.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace CCP.Api.IntegrationTests.Documents;

/// <summary>
/// The S3 provider, against a real S3-compatible bucket.
/// <para>
/// <b>Every other document test runs against the local provider</b> (debt #36),
/// which means the production storage path — the one that actually holds a
/// company's documents — was verified by reading it. ADR-014 asks specifically
/// not to rely on that, and three of its claims cannot be checked any other way:
/// that a pre-signed URL works, that it expires, and that the bucket is
/// unreadable without one.
/// </para>
/// <para>
/// That last is the load-bearing one. <b>A public bucket would make every access
/// rule in the Documents module decorative</b>, because anybody who learned a key
/// could fetch the file without ever reaching the Platform — and the key is in
/// the URL of every download the Platform has ever issued.
/// </para>
/// <para>
/// Needs an S3-compatible service, the same way the rest of this suite needs
/// PostgreSQL. CI provides MinIO; a machine without it cannot run this suite at
/// all, which is already the situation (#70).
/// </para>
/// </summary>
public sealed class ObjectStorageTests : IAsyncLifetime, IDisposable
{
    /// <summary>
    /// Matches <c>docker-compose.yml</c>, and is overridden in CI. Not a secret:
    /// it reaches a throwaway container on a developer's own machine, and the
    /// value is in the compose file beside it.
    /// </summary>
    private static string ServiceUrl =>
        Environment.GetEnvironmentVariable("CCP_TEST_S3_URL") ?? "http://localhost:9000";

    private static string AccessKey =>
        Environment.GetEnvironmentVariable("CCP_TEST_S3_ACCESS_KEY") ?? "ccp_dev";

    private static string SecretKey =>
        Environment.GetEnvironmentVariable("CCP_TEST_S3_SECRET_KEY") ?? "ccp_dev_local_only";

    private readonly string _bucket = $"ccp-test-{Guid.NewGuid():N}"[..24];

    private AmazonS3Client _client = null!;
    private S3StorageProvider _storage = null!;

    public async Task InitializeAsync()
    {
        _client = new AmazonS3Client(
            AccessKey,
            SecretKey,
            new AmazonS3Config { ServiceURL = ServiceUrl, ForcePathStyle = true });

        await _client.PutBucketAsync(
            new PutBucketRequest { BucketName = _bucket, UseClientRegion = false });

        _storage = new S3StorageProvider(Options.Create(new S3StorageOptions
        {
            BucketName = _bucket,
            ServiceUrl = ServiceUrl,
            AccessKeyId = AccessKey,
            SecretAccessKey = SecretKey,
            UsePathStyle = true
        }));
    }

    public async Task DisposeAsync()
    {
        // Emptied before it is removed: S3 refuses to delete a bucket with
        // anything in it, and a leaked bucket per test run is a bill.
        ListObjectsV2Response listing = await _client.ListObjectsV2Async(
            new ListObjectsV2Request { BucketName = _bucket });

        foreach (S3Object item in listing.S3Objects ?? [])
        {
            await _client.DeleteObjectAsync(_bucket, item.Key);
        }

        await _client.DeleteBucketAsync(_bucket);

        Dispose();
    }

    /// <summary>
    /// The synchronous half, because the clients dispose synchronously and the
    /// analyser is right that a type owning them must say so.
    /// </summary>
    public void Dispose()
    {
        _storage?.Dispose();
        _client?.Dispose();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task WhatIsStoredComesBackByteForByte()
    {
        byte[] content = Encoding.UTF8.GetBytes("The quick brown fox جملة عربية");

        await _storage.StoreAsync("aa/round-trip", new MemoryStream(content), "text/plain");

        await using Stream? read = await _storage.OpenAsync("aa/round-trip");

        Assert.NotNull(read);

        using var buffer = new MemoryStream();
        await read.CopyToAsync(buffer);

        Assert.Equal(content, buffer.ToArray());
    }

    /// <summary>
    /// A row pointing at an object that is not there is a real state — a
    /// database restored from a backup newer than the bucket — so the provider
    /// says so plainly rather than throwing. Only a real service exercises the
    /// 404 path this depends on.
    /// </summary>
    [Fact]
    public async Task AnObjectThatIsNotThereReadsAsNothing()
        => Assert.Null(await _storage.OpenAsync("aa/never-written"));

    /// <summary>
    /// Removal is idempotent, which is what the purge sweep relies on when it
    /// retries: the bytes go first and the record follows, so a failure between
    /// them means the next sweep deletes what is already gone.
    /// </summary>
    [Fact]
    public async Task RemovingWhatIsAlreadyGoneSucceeds()
    {
        await _storage.DeleteAsync("aa/never-written");
        await _storage.DeleteAsync("aa/never-written");
    }

    [Fact]
    public async Task ListingFindsWhatWasStored()
    {
        await _storage.StoreAsync("aa/listed", new MemoryStream([1, 2, 3]), "application/octet-stream");

        List<StoredObject> objects = [];

        await foreach (StoredObject item in _storage.ListAsync())
        {
            objects.Add(item);
        }

        StoredObject stored = Assert.Single(objects, item => item.ObjectKey == "aa/listed");

        Assert.Equal(3, stored.Length);
    }

    // -----------------------------------------------------------------------
    // Pre-signed URLs
    // -----------------------------------------------------------------------

    /// <summary>
    /// The URL is built with the scheme the endpoint actually uses.
    /// <para>
    /// <b>The defect this suite found on its first run.</b> The SDK's
    /// <c>GetPreSignedUrlRequest.Protocol</c> defaults to HTTPS whatever
    /// <c>ServiceURL</c> says, so a deployment reaching its object storage over
    /// plain HTTP issued download links that could not be fetched at all — while
    /// every API call worked, because those honour the endpoint. Only the links
    /// handed to browsers were wrong, which is the half no unit test touches.
    /// </para>
    /// </summary>
    [Fact]
    public async Task APreSignedUrlUsesTheSchemeTheEndpointUses()
    {
        await _storage.StoreAsync("aa/scheme", new MemoryStream([1]), "application/pdf");

        Uri? url = await _storage.TryCreateReadUrlAsync(
            "aa/scheme", "a.pdf", "application/pdf", TimeSpan.FromMinutes(5));

        Assert.NotNull(url);
        Assert.Equal(new Uri(ServiceUrl).Scheme, url.Scheme);
    }

    [Fact]
    public async Task APreSignedUrlFetchesTheFile()
    {
        byte[] content = Encoding.UTF8.GetBytes("contract");

        await _storage.StoreAsync("aa/signed", new MemoryStream(content), "application/pdf");

        Uri? url = await _storage.TryCreateReadUrlAsync(
            "aa/signed", "contract-final.pdf", "application/pdf", TimeSpan.FromMinutes(5));

        Assert.NotNull(url);

        using var http = new HttpClient();
        using HttpResponseMessage response = await http.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(content, await response.Content.ReadAsByteArrayAsync());
    }

    /// <summary>
    /// The file arrives with the name the person uploaded, and as a download.
    /// <para>
    /// The disposition is set on the URL rather than left to the browser, so a
    /// document is saved rather than rendered in the tab — which for anything
    /// that can contain script is the difference between a file and a page
    /// running on the storage domain.
    /// </para>
    /// </summary>
    [Fact]
    public async Task APreSignedUrlCarriesTheFileNameAndForcesADownload()
    {
        await _storage.StoreAsync("aa/named", new MemoryStream([1]), "application/pdf");

        Uri? url = await _storage.TryCreateReadUrlAsync(
            "aa/named", "contract-final.pdf", "application/pdf", TimeSpan.FromMinutes(5));

        using var http = new HttpClient();
        using HttpResponseMessage response = await http.GetAsync(url!);

        string? disposition = response.Content.Headers.ContentDisposition?.ToString();

        Assert.NotNull(disposition);
        Assert.Contains("attachment", disposition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("contract-final.pdf", disposition, StringComparison.Ordinal);
    }

    /// <summary>
    /// A name carrying a quotation mark does not end the header early.
    /// <para>
    /// The name came from whoever uploaded the file. A quotation mark or a
    /// newline in it would close the header and let the rest be read as another
    /// one — and this is the only place that can be checked against a server
    /// that actually parses the header it is sent.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AFileNameCannotBreakOutOfTheHeader()
    {
        await _storage.StoreAsync("aa/nasty", new MemoryStream([1]), "application/pdf");

        Uri? url = await _storage.TryCreateReadUrlAsync(
            "aa/nasty", "a\"; x=\"b\r\nX-Injected: yes", "application/pdf", TimeSpan.FromMinutes(5));

        using var http = new HttpClient();
        using HttpResponseMessage response = await http.GetAsync(url!);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Injected"));
    }

    /// <summary>
    /// <b>The assertion this whole class exists for.</b>
    /// <para>
    /// The bucket is never publicly readable. A public one would make every
    /// access rule in the Documents module decorative: anybody who learned a key
    /// could fetch the file without ever reaching the Platform, and the key is in
    /// the URL of every download it has ever issued.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheSameAddressWithoutASignatureIsRefused()
    {
        await _storage.StoreAsync("aa/private", new MemoryStream([1]), "application/pdf");

        Uri? signed = await _storage.TryCreateReadUrlAsync(
            "aa/private", "private.pdf", "application/pdf", TimeSpan.FromMinutes(5));

        // The same object, with the signature and everything else stripped.
        var bare = new Uri($"{ServiceUrl.TrimEnd('/')}/{_bucket}/aa/private");

        using var http = new HttpClient();
        using HttpResponseMessage response = await http.GetAsync(bare);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);

        // And the signed one works, so this is a test of the signature rather
        // than of a bucket name that was wrong all along.
        using HttpResponseMessage allowed = await http.GetAsync(signed!);

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    /// <summary>
    /// And the URL stops working.
    /// <para>
    /// A pre-signed URL <b>is</b> the authorization: anybody holding it can fetch
    /// the file, and it will end up in a browser history, a proxy log and
    /// occasionally a chat message. The expiry is the only thing that makes that
    /// acceptable, and it is enforced by the storage service rather than by the
    /// Platform — so this is the only place it can be checked.
    /// </para>
    /// </summary>
    [Fact]
    public async Task APreSignedUrlStopsWorkingWhenItExpires()
    {
        await _storage.StoreAsync("aa/expiring", new MemoryStream([1]), "application/pdf");

        Uri? url = await _storage.TryCreateReadUrlAsync(
            "aa/expiring", "expiring.pdf", "application/pdf", TimeSpan.FromSeconds(-30));

        using var http = new HttpClient();
        using HttpResponseMessage response = await http.GetAsync(url!);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
