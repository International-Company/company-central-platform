using CCP.Modules.Documents.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Documents.UnitTests;

/// <summary>
/// The provider must be constructible without touching the filesystem.
/// <para>
/// <b>This pins a defect that took the whole Platform down.</b> The provider is a
/// singleton resolved while the host is being built, and its constructor created
/// its root directory. In the container that directory sits under a path owned by
/// root while the process runs as an unprivileged user, so the call was refused —
/// and identity, authorization, workflow and everything else failed to start
/// because documents could not create a folder.
/// </para>
/// <para>
/// A storage problem must cost the company its documents and nothing else.
/// </para>
/// </summary>
public sealed class LocalStorageStartupTests
{
    [Fact]
    public void ConstructingTheProviderCreatesNothing()
    {
        string root = Path.Combine(
            Path.GetTempPath(), $"ccp-startup-{Guid.NewGuid():N}", "nested", "deeper");

        _ = new LocalFileStorageProvider(
            Options.Create(new LocalStorageOptions { RootPath = root }),
            NullLogger<LocalFileStorageProvider>.Instance);

        // Nothing on disk. A deployment that never uploads a document never
        // touches the filesystem at all.
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void AnUnwritablePathDoesNotStopTheProviderBeingBuilt()
    {
        // A path that cannot be created. On Linux this is a directory under a
        // file; on Windows it is an invalid name. Either way the constructor
        // must not care, because it must not look.
        string impossible = Path.Combine(Path.GetTempPath(), "ccp-not-a-directory", "\0", "store");

        Exception? thrown = Record.Exception(() => new LocalFileStorageProvider(
            Options.Create(new LocalStorageOptions { RootPath = impossible }),
            NullLogger<LocalFileStorageProvider>.Instance));

        Assert.Null(thrown);
    }

    [Fact]
    public void TheDefaultRootIsSomewhereTheProcessCanActuallyWrite()
    {
        // Not beside the application. The container image runs as an
        // unprivileged user and the application directory belongs to root, so a
        // default under it is a default that fails in the one place the Platform
        // actually runs.
        var options = new LocalStorageOptions();

        Assert.StartsWith(
            Path.GetTempPath(), options.RootPath, StringComparison.Ordinal);

        Assert.DoesNotContain(
            AppContext.BaseDirectory, options.RootPath, StringComparison.Ordinal);
    }
}
