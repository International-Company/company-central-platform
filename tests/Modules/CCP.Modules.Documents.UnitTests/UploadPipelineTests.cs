using System.Security.Cryptography;
using System.Text;
using CCP.Kernel.Results;
using CCP.Modules.Documents.Application;
using CCP.Modules.Documents.Application.Abstractions;
using CCP.Modules.Documents.Application.Storage;
using CCP.Modules.Documents.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Documents.UnitTests;

/// <summary>
/// Everything that happens to a file between arriving and being stored.
/// <para>
/// Run against the real local storage provider in a temporary directory rather
/// than a substitute, because half of what is being tested is whether the bytes
/// actually end up somewhere.
/// </para>
/// </summary>
public sealed class UploadPipelineTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"ccp-documents-tests-{Guid.NewGuid():N}");

    private readonly LocalFileStorageProvider _storage;

    public UploadPipelineTests() =>
        _storage = new LocalFileStorageProvider(
            Options.Create(new LocalStorageOptions { RootPath = _root }));

    private DocumentContentService AService(
        IDocumentScanner? scanner = null, DocumentOptions? options = null) =>
        new(_storage, scanner ?? new NoOpDocumentScanner(), options ?? new DocumentOptions());

    private static MemoryStream APdf(int padding = 0) =>
        new(Encoding.UTF8.GetBytes("%PDF-1.7\n" + new string('x', padding)));

    [Fact]
    public async Task AGoodFileIsStoredUnderAKeyThatSaysNothingAboutIt()
    {
        Result<DocumentContentService.StoredContent> stored =
            await AService().AcceptAsync(APdf(), "quarterly report.pdf", "application/pdf");

        Assert.True(stored.IsSuccess);
        Assert.Equal("application/pdf", stored.Value.ContentType);

        // The name is metadata; it must not appear in the key. A key built from
        // a name invites path traversal and collisions.
        Assert.DoesNotContain("quarterly", stored.Value.ObjectKey, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("report", stored.Value.ObjectKey, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("..", stored.Value.ObjectKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheHashIsOfWhatWasActuallyStored()
    {
        using MemoryStream content = APdf(200);
        byte[] expected = SHA256.HashData(content.ToArray());
        content.Position = 0;

        Result<DocumentContentService.StoredContent> stored =
            await AService().AcceptAsync(content, "report.pdf", null);

        Assert.Equal(Convert.ToHexStringLower(expected), stored.Value.Sha256);

        await using Stream? read = await _storage.OpenAsync(stored.Value.ObjectKey);

        Assert.NotNull(read);

        using var buffer = new MemoryStream();
        await read.CopyToAsync(buffer);

        Assert.Equal(expected, SHA256.HashData(buffer.ToArray()));
    }

    [Fact]
    public async Task AnExecutableIsRefusedHoweverItIsNamedAndDeclared()
    {
        using var content = new MemoryStream("MZ\0\0executable"u8.ToArray());

        Result<DocumentContentService.StoredContent> stored =
            await AService().AcceptAsync(content, "invoice.pdf", "application/pdf");

        Assert.True(stored.IsFailure);
        Assert.Equal("DOCUMENTS.FILE_TYPE_NOT_ALLOWED", stored.Error.Code);
    }

    [Fact]
    public async Task AnEmptyFileIsRefused()
    {
        using var content = new MemoryStream([]);

        Result<DocumentContentService.StoredContent> stored =
            await AService().AcceptAsync(content, "nothing.pdf", null);

        Assert.True(stored.IsFailure);
        Assert.Equal("DOCUMENTS.EMPTY_FILE", stored.Error.Code);
    }

    [Fact]
    public async Task TheSizeLimitStopsTheCopyRatherThanCheckingAfterwards()
    {
        var options = new DocumentOptions { MaxFileSizeInBytes = 1024 };

        Result<DocumentContentService.StoredContent> stored =
            await AService(options: options).AcceptAsync(APdf(4096), "big.pdf", null);

        Assert.True(stored.IsFailure);
        Assert.Equal("DOCUMENTS.FILE_TOO_LARGE", stored.Error.Code);

        // Nothing reached the store. A refused upload must leave no object
        // behind for somebody to find later and wonder about.
        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ADeliberateTypeMismatchIsRefused()
    {
        // The detected type is what would be stored anyway, so this is not about
        // choosing between them: a caller insisting a PDF is a PNG is worth
        // refusing loudly.
        Result<DocumentContentService.StoredContent> stored =
            await AService().AcceptAsync(APdf(), "report.pdf", "image/png");

        Assert.True(stored.IsFailure);
        Assert.Equal("DOCUMENTS.DECLARED_TYPE_MISMATCH", stored.Error.Code);
    }

    [Fact]
    public async Task AVagueDeclaredTypeIsAccepted()
    {
        // A browser sending application/octet-stream for an unfamiliar extension
        // is not lying, and refusing it would break uploads for no gain.
        Result<DocumentContentService.StoredContent> stored =
            await AService().AcceptAsync(APdf(), "report.pdf", "application/octet-stream");

        Assert.True(stored.IsSuccess);
    }

    [Fact]
    public async Task TheDefaultScannerSaysNotScannedRatherThanClean()
    {
        Result<DocumentContentService.StoredContent> stored =
            await AService().AcceptAsync(APdf(), "report.pdf", null);

        // The verdict is recorded, so nobody reading the log later concludes a
        // file was checked when nothing looked at it.
        Assert.Equal(ScanVerdict.NotScanned, stored.Value.ScanVerdict);
    }

    [Fact]
    public async Task AnInfectedFileNeverReachesTheStore()
    {
        Result<DocumentContentService.StoredContent> stored =
            await AService(new RejectingScanner()).AcceptAsync(APdf(), "report.pdf", null);

        Assert.True(stored.IsFailure);
        Assert.Equal("DOCUMENTS.SCAN_REJECTED", stored.Error.Code);
        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task AScannerThatThrowsIsAFailureAndNotAPass()
    {
        // Swallowing this into a pass is how an outage becomes an infection.
        Result<DocumentContentService.StoredContent> stored =
            await AService(new ThrowingScanner()).AcceptAsync(APdf(), "report.pdf", null);

        Assert.True(stored.IsFailure);
        Assert.Equal("DOCUMENTS.SCAN_REJECTED", stored.Error.Code);
    }

    [Fact]
    public async Task AScannerOutageCanBeAllowedThroughDeliberately()
    {
        var options = new DocumentOptions { RejectWhenScannerUnavailable = false };

        Result<DocumentContentService.StoredContent> stored =
            await AService(new ThrowingScanner(), options).AcceptAsync(APdf(), "report.pdf", null);

        Assert.True(stored.IsSuccess);
        Assert.Equal(ScanVerdict.Failed, stored.Value.ScanVerdict);
    }

    [Fact]
    public async Task DeletingContentThatIsAlreadyGoneSucceeds()
    {
        // The purge sweep depends on this: it deletes the bytes before it marks
        // the record, so a retry must be able to finish.
        await _storage.DeleteAsync("ab/does-not-exist");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private sealed class RejectingScanner : IDocumentScanner
    {
        public string Name => "rejecting";

        public Task<ScanResult> ScanAsync(Stream content, CancellationToken cancellationToken = default)
            => Task.FromResult(ScanResult.Infected("EICAR-Test-File"));
    }

    private sealed class ThrowingScanner : IDocumentScanner
    {
        public string Name => "unreachable";

        public Task<ScanResult> ScanAsync(Stream content, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("the scanning service refused the connection");
    }
}
