using CCP.Modules.Documents.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Documents.Infrastructure.Storage;

/// <summary>Where the local provider keeps its files.</summary>
public sealed class LocalStorageOptions
{
    public const string SectionName = "Documents:LocalStorage";

    /// <summary>
    /// The directory the objects live under.
    /// <para>
    /// <b>Defaults to a temporary directory, and that default is deliberate
    /// rather than lazy.</b> It used to be a folder beside the application, which
    /// worked everywhere except the place the Platform actually runs: the
    /// container image runs as an unprivileged user and <c>/app</c> belongs to
    /// root, so creating a subdirectory there is refused — and because that
    /// happened while the container was starting, one module's optional storage
    /// default stopped the whole Platform from coming up.
    /// </para>
    /// <para>
    /// A temporary directory is writable by whoever is running and is not
    /// durable, which is exactly what an unconfigured deployment should get: it
    /// works, it says so in the log, and nothing pretends the files are safe.
    /// A deployment that keeps real documents configures object storage, or
    /// points this at a mounted volume.
    /// </para>
    /// </summary>
    public string RootPath { get; set; } =
        Path.Combine(Path.GetTempPath(), "ccp-document-store");
}

/// <summary>
/// Objects as files in a directory.
/// <para>
/// The development implementation, and the fallback when no object storage is
/// configured. It is deliberately the simplest thing that satisfies the
/// interface: no pre-signed URLs, no lifecycle rules, no redundancy.
/// </para>
/// <para>
/// <b>It is not a toy.</b> A small company running one instance with a mounted
/// volume can use it in production and be no worse off than it was with a file
/// share — as long as somebody backs the volume up, which is precisely the thing
/// object storage does for you and this does not.
/// </para>
/// </summary>
public sealed class LocalFileStorageProvider : IDocumentStorageProvider
{
    private readonly string _rootPath;
    private readonly ILogger<LocalFileStorageProvider> _logger;

    /// <summary>
    /// <para>
    /// <b>Does no I/O.</b> That is the whole of the fix for a defect that took
    /// the Platform down: this provider is a singleton resolved while the host
    /// is being built, so a constructor that touched the filesystem made an
    /// unwritable directory fatal to every module at once — identity,
    /// authorization, workflow, all of it, because documents could not create a
    /// folder.
    /// </para>
    /// <para>
    /// A storage problem must cost the company its documents and nothing else.
    /// The directory is created on the first write instead, where a failure
    /// fails that upload.
    /// </para>
    /// </summary>
    public LocalFileStorageProvider(
        IOptions<LocalStorageOptions> options,
        ILogger<LocalFileStorageProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _rootPath = options.Value.RootPath;
        _logger = logger;
    }

    public string Name => "local";

    public async Task StoreAsync(
        string objectKey,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        string path = ResolvePath(objectKey);

        EnsureRootExists();

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // CreateNew, not Create. An object key that already exists means either
        // a collision in twenty random bytes or a bug, and both deserve an
        // exception rather than silently overwriting somebody else's document.
        await using var file = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 64 * 1024, useAsync: true);

        await content.CopyToAsync(file, cancellationToken);
    }

    public Task<Stream?> OpenAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        string path = ResolvePath(objectKey);

        if (!File.Exists(path))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, useAsync: true);

        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        string path = ResolvePath(objectKey);

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Always null: a directory on a disk has no way to hand out a URL.
    /// <para>
    /// Which is what the nullable return is for. The caller streams the content
    /// instead, and nothing above this line has to know which kind of store it
    /// is talking to.
    /// </para>
    /// </summary>
    public Task<Uri?> TryCreateReadUrlAsync(
        string objectKey,
        string fileName,
        string contentType,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Uri?>(null);

    /// <summary>
    /// Creates the root on first use, and says once that it is not durable.
    /// <para>
    /// Here rather than in the constructor so that a deployment which never
    /// uploads a document never touches the filesystem — and so that a directory
    /// it cannot create fails an upload rather than a startup.
    /// </para>
    /// </summary>
    private void EnsureRootExists()
    {
        if (Directory.Exists(_rootPath))
        {
            return;
        }

        Directory.CreateDirectory(_rootPath);

        if (_logger.IsEnabled(LogLevel.Warning))
        {
            // Worth a warning every time it has to be created, because on a
            // container that is every restart — which is the operator's signal
            // that yesterday's documents are gone.
            _logger.LogWarning(
                "Documents are being stored on the local filesystem at {RootPath}. This is not "
                + "durable on a container. Configure Documents:S3 for anything that must survive "
                + "a restart.",
                _rootPath);
        }
    }

    /// <summary>
    /// Turns an object key into a path, and refuses anything that could escape.
    /// <para>
    /// The keys are generated by the Platform and contain nothing but hex and
    /// one separator, so this check should never fire. It is here because "this
    /// value is always safe" is a statement about today's callers, and the cost
    /// of being wrong once is every file on the disk.
    /// </para>
    /// </summary>
    private string ResolvePath(string objectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        string root = Path.GetFullPath(_rootPath);
        string candidate = Path.GetFullPath(Path.Combine(root, objectKey));

        if (!candidate.StartsWith(root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The object key '{objectKey}' resolves outside the document store.");
        }

        return candidate;
    }
}
