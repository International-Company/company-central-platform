using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Application.Events;
using CCP.Kernel.Paging;
using CCP.Modules.Documents.Domain.Access;
using CCP.Modules.Documents.Domain.Auditing;
using CCP.Modules.Documents.Domain.Documents;
using CCP.Modules.Documents.Domain.Linking;

namespace CCP.Modules.Documents.Application.Abstractions;

/// <summary>Commits the module's changes as one transaction.</summary>
public interface IDocumentUnitOfWork : IUnitOfWork;

/// <summary>The module's slice of the transactional outbox.</summary>
public interface IDocumentOutbox : IOutbox;

/// <summary>
/// Where the bytes live.
/// <para>
/// <b>The seam that keeps the Platform off any one vendor</b> (§18.1). A local
/// filesystem in development, S3-compatible storage in production, and neither
/// is visible above this line: no handler knows whether it is talking to a disk
/// or a bucket, and moving between them is a registration change.
/// </para>
/// <para>
/// The interface is deliberately about objects and not about files. There is no
/// directory, no rename, no listing — the metadata is in PostgreSQL and asking
/// the store to also be a catalogue is how two sources of truth get created.
/// </para>
/// </summary>
public interface IDocumentStorageProvider
{
    /// <summary>A name for the log, so "where was this stored" has an answer.</summary>
    string Name { get; }

    /// <summary>
    /// Writes content under a key the caller has already generated.
    /// <para>
    /// The key comes from the caller because it belongs on the version row, and
    /// a store that invented its own would make the row depend on a successful
    /// write to know what to record.
    /// </para>
    /// </summary>
    Task StoreAsync(
        string objectKey,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens stored content for reading, or null when the object is not there.
    /// <para>
    /// Null rather than an exception: an object missing from the store while its
    /// row exists is a real state — a restored database backup that is newer
    /// than the bucket — and the caller can say so plainly.
    /// </para>
    /// </summary>
    Task<Stream?> OpenAsync(string objectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes content permanently. Idempotent: removing what is already gone
    /// succeeds, because a purge that has to be retried must be able to finish.
    /// </summary>
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// A short-lived URL the browser may fetch directly, or null when this store
    /// cannot issue one.
    /// <para>
    /// Worth having because a large file streamed through the application ties
    /// up a request thread for as long as the download takes, and a hundred
    /// people downloading a video is an outage. Worth making optional because
    /// the local provider has no way to offer it, and an interface that demanded
    /// it would force the development implementation to invent a second HTTP
    /// server.
    /// </para>
    /// <para>
    /// Null is not a failure. The caller falls back to streaming the content
    /// itself, which always works.
    /// </para>
    /// </summary>
    Task<Uri?> TryCreateReadUrlAsync(
        string objectKey,
        string fileName,
        string contentType,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Inspects content for malware before it is stored.
/// <para>
/// <b>A hook, with a default that says no.</b> The Platform ships without a
/// scanner because bundling one would mean choosing a vendor, a licence and a
/// deployment shape on behalf of every company that installs this. What it does
/// not do is pretend: the default implementation reports
/// <see cref="ScanVerdict.NotScanned"/>, which is recorded as such, so nobody
/// reading a version row later concludes a file was checked when it was not.
/// </para>
/// </summary>
public interface IDocumentScanner
{
    /// <summary>A name for the record of what did the scanning.</summary>
    string Name { get; }

    /// <summary>
    /// Examines content that has been buffered and not yet stored.
    /// <para>
    /// The stream is seekable and positioned at the start, and the scanner must
    /// leave it that way — it is the same stream that will be written to
    /// storage, and rewinding it afterwards is the caller relying on the
    /// implementation being polite.
    /// </para>
    /// </summary>
    Task<ScanResult> ScanAsync(Stream content, CancellationToken cancellationToken = default);
}

/// <summary>What a scanner concluded.</summary>
/// <param name="Verdict">The conclusion.</param>
/// <param name="Detail">What it said, for the record and for the refusal message.</param>
public readonly record struct ScanResult(ScanVerdict Verdict, string? Detail = null)
{
    public static ScanResult Clean { get; } = new(ScanVerdict.Clean);

    public static ScanResult NotScanned { get; } = new(ScanVerdict.NotScanned);

    public static ScanResult Infected(string detail) => new(ScanVerdict.Infected, detail);

    /// <summary>
    /// A scanner that could not reach its service.
    /// <para>
    /// Distinct from <see cref="ScanVerdict.NotScanned"/> because they call for
    /// opposite responses: nobody configured a scanner is a decision, and the
    /// scanner being down is an incident.
    /// </para>
    /// </summary>
    public static ScanResult Failed(string detail) => new(ScanVerdict.Failed, detail);

    /// <summary>Whether the upload may proceed.</summary>
    public bool Rejects => Verdict is ScanVerdict.Infected;
}

/// <summary>A scanner's conclusion about a file.</summary>
public enum ScanVerdict
{
    /// <summary>No scanner is configured. The file was not examined.</summary>
    NotScanned = 0,

    /// <summary>Examined and found nothing.</summary>
    Clean = 1,

    /// <summary>Examined and refused.</summary>
    Infected = 2,

    /// <summary>A scanner is configured and could not answer.</summary>
    Failed = 3
}

/// <summary>
/// Reading and writing the module's own tables.
/// </summary>
public interface IDocumentRepository
{
    Task<Document?> GetAsync(Guid documentId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Document> Items, long Total)> SearchAsync(
        DocumentSearch search, CancellationToken cancellationToken = default);

    void Add(Document document);

    Task<IReadOnlyList<DocumentAccessRule>> GetRulesAsync(
        Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The rules on several documents at once.
    /// <para>
    /// Exists because a list of twenty-five documents needs the rules for each
    /// to say what the caller may do with it, and asking twenty-five times is
    /// how a list endpoint becomes the slowest thing in the Platform.
    /// </para>
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<DocumentAccessRule>>> GetRulesForAsync(
        IReadOnlyList<Guid> documentIds, CancellationToken cancellationToken = default);

    Task<DocumentAccessRule?> GetRuleAsync(
        Guid ruleId, CancellationToken cancellationToken = default);

    void AddRule(DocumentAccessRule rule);

    void RemoveRule(DocumentAccessRule rule);

    Task<IReadOnlyList<DocumentLink>> GetLinksAsync(
        Guid documentId, CancellationToken cancellationToken = default);

    Task<DocumentLink?> GetLinkAsync(
        Guid documentId, string resourceType, string resourceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Document>> GetLinkedDocumentsAsync(
        string resourceType, string resourceId, CancellationToken cancellationToken = default);

    void AddLink(DocumentLink link);

    void RemoveLink(DocumentLink link);

    void AddAccessLog(DocumentAccessLog entry);

    Task<(IReadOnlyList<DocumentAccessLog> Items, long Total)> GetAccessLogAsync(
        Guid documentId, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Documents whose grace period has run out, oldest first.
    /// <para>
    /// Bounded, because a sweep that loads everything eligible would, on the
    /// first run after somebody empties a department, try to purge ten thousand
    /// documents in one transaction.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Document>> GetDuePurgesAsync(
        DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default);
}

/// <summary>
/// What a document search is allowed to look at.
/// </summary>
/// <param name="Term">Matches title and file name. Optional.</param>
/// <param name="Category">The owner's own label. Optional.</param>
/// <param name="OrganizationUnitId">One unit exactly. Optional.</param>
/// <param name="IncludeDeleted">Whether documents awaiting purge are included.</param>
/// <param name="Caller">
/// Who is asking, so the query returns what they may see rather than what
/// exists. <b>Not optional.</b> A search that could be run without it would
/// eventually be run without it.
/// </param>
/// <param name="UnrestrictedByScope">
/// Whether the caller's permission reaches the whole company, in which case
/// access rules are not what limits the result.
/// </param>
public sealed record DocumentSearch(
    string? Term,
    string? Category,
    Guid? OrganizationUnitId,
    bool IncludeDeleted,
    AccessSubject Caller,
    bool UnrestrictedByScope,
    PageRequest Page);

/// <summary>
/// Everything about a person that an access rule can be written against.
/// <para>
/// Assembled once per request and then used both to decide a single document
/// and to filter a list. Those two answers being computed from the same value
/// is what stops a document appearing in a search that its owner cannot then
/// open, which is the classic way access checks and list filters drift apart.
/// </para>
/// </summary>
/// <param name="UserId">The caller.</param>
/// <param name="RoleIds">Every role they hold, at any scope.</param>
/// <param name="UnitId">The unit they sit in, or null when they sit in none.</param>
/// <param name="UnitChainIds">
/// That unit and every unit above it, nearest last. A rule granted to a
/// division with <c>IncludesSubUnits</c> reaches somebody in a team three
/// levels down precisely when the division appears in this list.
/// </param>
public sealed record AccessSubject(
    Guid UserId,
    IReadOnlyList<Guid> RoleIds,
    Guid? UnitId,
    IReadOnlyList<Guid> UnitChainIds)
{
    /// <summary>A caller with no roles and no place in the company.</summary>
    public static AccessSubject Bare(Guid userId) => new(userId, [], null, []);
}

/// <summary>
/// Works out who the caller is, in the terms access rules are written in.
/// <para>
/// Implemented in the infrastructure layer, because answering it means asking
/// Organization and Authorization and this module does not reference either
/// (§6.2). The same shape as Workflow's assignee resolver, and for the same
/// reason: the rule lives here, the lookup lives at the composition root.
/// </para>
/// </summary>
public interface IAccessSubjectResolver
{
    Task<AccessSubject> ResolveAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// How far the caller's permission reaches, in the one form this module needs.
/// <para>
/// The kernel's <c>ScopeFilter</c> says the same thing more precisely, and it
/// lives in the API layer where the Application layer cannot see it. This is the
/// single bit of it that matters here: a caller whose grant covers the whole
/// company does not need to be named on each document, and everybody else does.
/// </para>
/// <para>
/// Deliberately not "which units may they reach". Documents are shared by rule,
/// not by organizational position, and treating a unit-scoped permission as
/// access to every document in that unit would hand a department head every
/// private letter written to anybody who reports to them.
/// </para>
/// </summary>
public interface ICallerScope
{
    bool ReachesWholeCompany { get; }
}
