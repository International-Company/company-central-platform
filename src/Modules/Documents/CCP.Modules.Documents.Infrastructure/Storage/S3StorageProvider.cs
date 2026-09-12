using System.Runtime.CompilerServices;
using Amazon.S3;
using Amazon.S3.Model;
using CCP.Modules.Documents.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Documents.Infrastructure.Storage;

/// <summary>
/// How to reach an S3-compatible bucket.
/// <para>
/// <b>No credentials have defaults.</b> Every value here comes from the
/// environment and none of them is written down anywhere in this repository
/// (§21.1). A missing key means the provider is not configured, which means the
/// Platform uses local storage — never a partially configured client that fails
/// on the first upload.
/// </para>
/// </summary>
public sealed class S3StorageOptions
{
    public const string SectionName = "Documents:S3";

    /// <summary>The bucket. Configured means "use S3".</summary>
    public string? BucketName { get; set; }

    /// <summary>
    /// The endpoint, for anything that is not Amazon.
    /// <para>
    /// "S3-compatible" is a real category — MinIO, Cloudflare R2, Backblaze,
    /// DigitalOcean — and supporting it is one field. Leaving this empty uses
    /// AWS itself.
    /// </para>
    /// </summary>
    public string? ServiceUrl { get; set; }

    public string? Region { get; set; }

    public string? AccessKeyId { get; set; }

    public string? SecretAccessKey { get; set; }

    /// <summary>
    /// Whether to address the bucket as a path rather than a subdomain.
    /// <para>
    /// Required by most self-hosted implementations and by anything reached at
    /// a bare IP address, where a bucket-named subdomain does not resolve.
    /// </para>
    /// </summary>
    public bool UsePathStyle { get; set; } = true;

    /// <summary>Whether enough has been supplied to build a client.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BucketName)
        && !string.IsNullOrWhiteSpace(AccessKeyId)
        && !string.IsNullOrWhiteSpace(SecretAccessKey);
}

/// <summary>
/// Objects in an S3-compatible bucket. The production implementation.
/// <para>
/// The bucket is <b>never</b> publicly readable (§18.3). Access is granted one
/// object at a time, for a few minutes, to a caller the Platform has already
/// checked — which is what <see cref="TryCreateReadUrlAsync"/> produces. A
/// public bucket would make every access rule in this module decorative, since
/// anybody who learned a key could fetch the file without ever reaching the
/// Platform.
/// </para>
/// </summary>
public sealed class S3StorageProvider : IDocumentStorageProvider, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucketName;

    public S3StorageProvider(IOptions<S3StorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        S3StorageOptions settings = options.Value;

        if (!settings.IsConfigured)
        {
            // Reached only if somebody registers this without the configuration
            // the registration checks for. Loud, and at startup, rather than on
            // the first upload of the day.
            throw new InvalidOperationException(
                "S3 storage is registered without a bucket name and credentials. "
                + "Supply Documents:S3 settings through the environment, or leave them "
                + "unset to use local storage.");
        }

        var configuration = new AmazonS3Config
        {
            ForcePathStyle = settings.UsePathStyle
        };

        if (!string.IsNullOrWhiteSpace(settings.ServiceUrl))
        {
            configuration.ServiceURL = settings.ServiceUrl;
        }

        if (!string.IsNullOrWhiteSpace(settings.Region))
        {
            configuration.AuthenticationRegion = settings.Region;
        }

        _client = new AmazonS3Client(
            settings.AccessKeyId, settings.SecretAccessKey, configuration);

        _bucketName = settings.BucketName!;
    }

    public string Name => "s3";

    public async Task StoreAsync(
        string objectKey,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        await _client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = objectKey,
                InputStream = content,
                ContentType = contentType,

                // The Platform decides who may read this, on every request. The
                // bucket's own answer must always be "nobody".
                CannedACL = S3CannedACL.Private
            },
            cancellationToken);
    }

    public async Task<Stream?> OpenAsync(
        string objectKey, CancellationToken cancellationToken = default)
    {
        try
        {
            GetObjectResponse response = await _client.GetObjectAsync(
                _bucketName, objectKey, cancellationToken);

            return response.ResponseStream;
        }
        catch (AmazonS3Exception exception)
            when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // A row pointing at an object that is not there. Real, and not an
            // error in this method: the caller says so plainly instead.
            return null;
        }
    }

    public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        // S3 deletion is already idempotent — removing a key that is not there
        // succeeds — which is what the purge sweep depends on when it retries.
        await _client.DeleteObjectAsync(_bucketName, objectKey, cancellationToken);
    }

    /// <summary>
    /// A URL good for one object, for a few minutes.
    /// <para>
    /// The content-disposition is set here rather than left to the browser, so
    /// the file arrives with the name the person uploaded instead of forty hex
    /// characters — and so a document is downloaded rather than rendered in the
    /// tab, which for anything that can contain script is the difference between
    /// a file and a page running on the storage domain.
    /// </para>
    /// </summary>
    public Task<Uri?> TryCreateReadUrlAsync(
        string objectKey,
        string fileName,
        string contentType,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow + lifetime,
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentType = contentType,
                ContentDisposition = $"attachment; filename=\"{SanitizeFileName(fileName)}\""
            }
        };

        string url = _client.GetPreSignedURL(request);

        return Task.FromResult<Uri?>(new Uri(url));
    }

    /// <summary>
    /// Every object in the bucket, a page at a time.
    /// <para>
    /// S3 returns at most a thousand keys per call, and the continuation token is
    /// the only way past that. A listing that stopped at the first page would
    /// report a bucket of a million objects as a bucket of a thousand — and a
    /// reconciliation built on it would call the other 999,000 accounted for.
    /// </para>
    /// </summary>
    public async IAsyncEnumerable<StoredObject> ListAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? continuationToken = null;

        do
        {
            ListObjectsV2Response page = await _client.ListObjectsV2Async(
                new ListObjectsV2Request
                {
                    BucketName = _bucketName,
                    ContinuationToken = continuationToken
                },
                cancellationToken);

            foreach (S3Object item in page.S3Objects ?? [])
            {
                yield return new StoredObject(
                    item.Key,
                    item.Size ?? 0,

                    // S3 reports this as a local DateTime. Read as UTC, because
                    // the sweep compares it against a grace period and an hour's
                    // error in the wrong direction is a deleted document.
                    new DateTimeOffset(
                        DateTime.SpecifyKind(
                            item.LastModified ?? DateTime.UtcNow, DateTimeKind.Utc)));
            }

            continuationToken = page.IsTruncated == true ? page.NextContinuationToken : null;
        }
        while (continuationToken is not null);
    }

    /// <summary>
    /// Makes a file name safe to put inside a header.
    /// <para>
    /// The name came from the uploader, and a quotation mark or a newline in it
    /// would end the header early and let the rest be read as another one. The
    /// stored name is left exactly as it was; only this copy is trimmed.
    /// </para>
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        Span<char> buffer = stackalloc char[Math.Min(fileName.Length, 200)];
        int written = 0;

        foreach (char character in fileName)
        {
            if (written == buffer.Length)
            {
                break;
            }

            buffer[written++] = char.IsControl(character) || character is '"' or '\\'
                ? '_'
                : character;
        }

        return written == 0 ? "download" : new string(buffer[..written]);
    }

    public void Dispose() => _client.Dispose();
}
