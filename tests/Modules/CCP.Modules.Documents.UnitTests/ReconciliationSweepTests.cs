using System.Diagnostics.Metrics;
using CCP.Kernel.Application.Jobs;
using CCP.Kernel.Application.Observability;
using CCP.Kernel.Primitives;
using CCP.Modules.Documents.Application;
using CCP.Modules.Documents.Application.Abstractions;
using CCP.Modules.Documents.Domain.Access;
using CCP.Modules.Documents.Domain.Auditing;
using CCP.Modules.Documents.Domain.Documents;
using CCP.Modules.Documents.Domain.Linking;
using CCP.Modules.Documents.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CCP.Modules.Documents.UnitTests;

/// <summary>
/// Finding content the database has never heard of.
/// <para>
/// <b>This is the price ADR-014 accepted when it chose two stores.</b> There is
/// no transaction spanning a bucket and a database, so an upload writes the
/// object and then the row, and a failure between them leaves content nothing
/// references. That order is right — the reverse leaves a row pointing at
/// nothing, which costs somebody their file where this costs storage. But
/// "costs storage" is only true while somebody is counting.
/// </para>
/// <para>
/// The assertion that matters most here is the one about an upload in progress.
/// Between the store and the commit, a perfectly good document is content with no
/// row, indistinguishable from an orphan by every measure except its age — and a
/// sweep that missed that would report, and a deleting version of it would
/// destroy, documents out from under the people uploading them.
/// </para>
/// </summary>
public sealed class ReconciliationSweepTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AnObjectNoVersionRefersToIsReported()
    {
        var storage = new StubStorage(
            new StoredObject("aa/orphan", 1024, Now.AddDays(-2)));

        string? summary = await SweepAsync(storage, known: Keys());

        Assert.NotNull(summary);
        Assert.Contains("1 orphaned", summary, StringComparison.Ordinal);
        Assert.Contains("1024 byte(s)", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnObjectAVersionRefersToIsNot()
    {
        var storage = new StubStorage(
            new StoredObject("aa/kept", 1024, Now.AddDays(-2)));

        string? summary = await SweepAsync(storage, known: Keys("aa/kept"));

        Assert.NotNull(summary);
        Assert.Contains("none orphaned", summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// The assertion this class exists for. An object written a minute ago is
    /// almost certainly an upload whose row has not committed yet, and calling it
    /// an orphan is how a reconciliation becomes a data-loss incident.
    /// </summary>
    [Fact]
    public async Task AnObjectWrittenMomentsAgoIsLeftAlone()
    {
        var storage = new StubStorage(
            new StoredObject("aa/in-flight", 1024, Now.AddMinutes(-1)));

        string? summary = await SweepAsync(storage, known: Keys());

        Assert.NotNull(summary);
        Assert.Contains("none orphaned", summary, StringComparison.Ordinal);

        // And said plainly rather than silently skipped, so a pass that judged
        // nothing does not read as a pass that found nothing.
        Assert.Contains("1 too recent to judge", summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// It reports and never removes. An orphan is defined by the database not
    /// knowing about it, which is also what every object in the bucket looks like
    /// when the database is a restored backup, or the wrong environment's.
    /// </summary>
    [Fact]
    public async Task NothingIsEverDeleted()
    {
        var storage = new StubStorage(
            new StoredObject("aa/orphan-one", 10, Now.AddDays(-2)),
            new StoredObject("aa/orphan-two", 20, Now.AddDays(-2)));

        await SweepAsync(storage, known: Keys());

        Assert.Empty(storage.Deleted);
    }

    /// <summary>
    /// More objects than one batch, so the batching is exercised rather than
    /// assumed. A sweep that checked only the first batch would report a bucket
    /// of ten thousand orphans as clean.
    /// </summary>
    [Fact]
    public async Task EveryObjectIsCheckedAcrossBatches()
    {
        StoredObject[] objects = [.. Enumerable
            .Range(0, 1201)
            .Select(i => new StoredObject($"aa/{i}", 1, Now.AddDays(-2)))];

        var storage = new StubStorage(objects);

        // Every third one is accounted for, so the answer is neither "all" nor
        // "none" and a sweep that lost a batch cannot land on it by luck.
        HashSet<string> known = [.. objects.Where((_, i) => i % 3 == 0).Select(o => o.ObjectKey)];

        string? summary = await SweepAsync(storage, known);

        Assert.NotNull(summary);
        Assert.Contains("Checked 1201 object(s)", summary, StringComparison.Ordinal);
        Assert.Contains($"{objects.Length - known.Count} orphaned", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyStoreReconcilesToNothing()
    {
        string? summary = await SweepAsync(new StubStorage(), known: Keys());

        Assert.NotNull(summary);
        Assert.Contains("Checked 0 object(s)", summary, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Harness
    // -----------------------------------------------------------------------

    private static IReadOnlySet<string> Keys(params string[] keys)
        => keys.ToHashSet(StringComparer.Ordinal);

    private static async Task<string?> SweepAsync(
        StubStorage storage, IReadOnlySet<string> known)
    {
        var services = new ServiceCollection();

        services.AddScoped<IDocumentRepository>(_ => new StubRepository(known));

        var sweep = new ReconciliationSweep(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            storage,
            new DocumentOptions(),
            new FixedClock(),
            new JobRunner(
                new NullJobJournal(),
                new PlatformMetrics(new StubMeterFactory()),
                new FixedClock(),
                NullLogger<JobRunner>.Instance),
            NullLogger<ReconciliationSweep>.Instance);

        return await sweep.SweepAsync(CancellationToken.None);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class StubMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = [];

        public Meter Create(MeterOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            var meter = new Meter(options.Name, options.Version);
            _meters.Add(meter);

            return meter;
        }

        public void Dispose()
        {
            foreach (Meter meter in _meters)
            {
                meter.Dispose();
            }

            _meters.Clear();
        }
    }

    private sealed class StubStorage(params StoredObject[] objects) : IDocumentStorageProvider
    {
        public List<string> Deleted { get; } = [];

        public string Name => "stub";

        public Task StoreAsync(
            string objectKey, Stream content, string contentType,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<Stream?> OpenAsync(string objectKey, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream?>(null);

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
        {
            Deleted.Add(objectKey);

            return Task.CompletedTask;
        }

        public Task<Uri?> TryCreateReadUrlAsync(
            string objectKey, string fileName, string contentType, TimeSpan lifetime,
            CancellationToken cancellationToken = default)
            => Task.FromResult<Uri?>(null);

#pragma warning disable CS1998 // No await: the stub has nothing to wait for.
        public async IAsyncEnumerable<StoredObject> ListAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            foreach (StoredObject item in objects)
            {
                yield return item;
            }
        }
#pragma warning restore CS1998
    }

    /// <summary>
    /// Answers only the question the sweep asks. Every other member throws: a
    /// stub that quietly returned empty would let the sweep start depending on
    /// something else without anybody noticing.
    /// </summary>
    private sealed class StubRepository(IReadOnlySet<string> known) : IDocumentRepository
    {
        public Task<IReadOnlySet<string>> GetKnownObjectKeysAsync(
            IReadOnlyCollection<string> objectKeys, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlySet<string>>(
                objectKeys.Where(known.Contains).ToHashSet(StringComparer.Ordinal));

        public Task<Document?> GetAsync(Guid documentId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<(IReadOnlyList<Document> Items, long Total)> SearchAsync(
            DocumentSearch search, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentAccessRule>> GetRulesAsync(
            Guid documentId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<DocumentAccessRule>>> GetRulesForAsync(
            IReadOnlyList<Guid> documentIds, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DocumentAccessRule?> GetRuleAsync(
            Guid ruleId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentLink>> GetLinksAsync(
            Guid documentId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DocumentLink?> GetLinkAsync(
            Guid documentId, string resourceType, string resourceId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Document>> GetLinkedDocumentsAsync(
            string resourceType, string resourceId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<(IReadOnlyList<DocumentAccessLog> Items, long Total)> GetAccessLogAsync(
            Guid documentId, int page, int pageSize, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Document>> GetDuePurgesAsync(
            DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Add(Document document) => throw new NotSupportedException();

        public void AddRule(DocumentAccessRule rule) => throw new NotSupportedException();

        public void RemoveRule(DocumentAccessRule rule) => throw new NotSupportedException();

        public void AddLink(DocumentLink link) => throw new NotSupportedException();

        public void RemoveLink(DocumentLink link) => throw new NotSupportedException();

        public void AddAccessLog(DocumentAccessLog entry) => throw new NotSupportedException();
    }
}
