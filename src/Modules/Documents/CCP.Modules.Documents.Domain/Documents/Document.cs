using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;

namespace CCP.Modules.Documents.Domain.Documents;

/// <summary>
/// A stored file, its history, and the record of who touched it.
/// <para>
/// <b>The row is not the file.</b> This holds the metadata; the bytes live in
/// object storage under a key nobody can guess (§18.1). Keeping them apart is
/// what lets a database backup stay a reasonable size, a document be served
/// without passing through the application, and content be destroyed while the
/// record of it having existed survives.
/// </para>
/// <para>
/// The Platform stores documents; it does not know what they mean. There is no
/// invoice here, no contract clause, no expiry rule — a business system links
/// its own record to a document and keeps the meaning on its own side.
/// </para>
/// </summary>
public sealed class Document : AggregateRoot, IAuditableEntity
{
    private readonly List<DocumentVersion> _versions = [];

    private Document() { }

    private Document(
        Guid id,
        string title,
        string? category,
        Guid ownerUserId,
        Guid? organizationUnitId,
        DateTimeOffset now)
        : base(id)
    {
        Title = title;
        Category = category;
        OwnerUserId = ownerUserId;
        OrganizationUnitId = organizationUnitId;
        Status = DocumentStatus.Active;
        CreatedAt = now;
    }

    /// <summary>What a person calls it. Free text, and not the file name.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>
    /// A label the owner chooses, meaning nothing to the Platform.
    /// <para>
    /// Free text rather than an enumeration on purpose: the Platform cannot know
    /// which kinds of document a company keeps, and a fixed list would be wrong
    /// for the first business system that used it.
    /// </para>
    /// </summary>
    public string? Category { get; private set; }

    public Guid OwnerUserId { get; private set; }

    /// <summary>
    /// Where in the company this document belongs, or null when it belongs to a
    /// person rather than a place.
    /// <para>
    /// The id and not the path. A unit that moves takes its documents with it,
    /// and a stored path would have to be rewritten across every row beneath
    /// that unit on every reorganization.
    /// </para>
    /// </summary>
    public Guid? OrganizationUnitId { get; private set; }

    public DocumentStatus Status { get; private set; }

    /// <summary>The highest version number issued. Versions start at 1.</summary>
    public int CurrentVersionNumber { get; private set; }

    public DateTimeOffset? MarkedForDeletionAt { get; private set; }

    public Guid? MarkedForDeletionBy { get; private set; }

    /// <summary>
    /// The moment the content may be destroyed, fixed when deletion is asked for.
    /// <para>
    /// Stored rather than computed at purge time, so shortening the configured
    /// grace period cannot retroactively destroy something already inside its
    /// old window.
    /// </para>
    /// </summary>
    public DateTimeOffset? PurgeAfter { get; private set; }

    public DateTimeOffset? PurgedAt { get; private set; }

    public IReadOnlyList<DocumentVersion> Versions => _versions.AsReadOnly();

    /// <summary>The newest version, or null before the first upload.</summary>
    public DocumentVersion? CurrentVersion =>
        _versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static Result<Document> Create(
        string title,
        string? category,
        Guid ownerUserId,
        Guid? organizationUnitId,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Result.Failure<Document>(DocumentErrors.TitleRequired);
        }

        if (ownerUserId == Guid.Empty)
        {
            return Result.Failure<Document>(DocumentErrors.OwnerRequired);
        }

        return Result.Success(new Document(
            Uuid7.NewGuid(now),
            title.Trim(),
            string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
            ownerUserId,
            organizationUnitId,
            now));
    }

    /// <summary>
    /// Record content that has already been inspected, scanned and stored.
    /// <para>
    /// The order is not arbitrary. The bytes are checked and written to object
    /// storage <i>before</i> this is called, so a version row that exists is one
    /// whose content exists. The other order leaves a row pointing at nothing,
    /// which costs somebody their file; this one can leave an unreferenced
    /// object, which costs a little storage and is swept up.
    /// </para>
    /// </summary>
    public Result<DocumentVersion> AddVersion(
        string fileName,
        string contentType,
        long sizeInBytes,
        string sha256,
        string objectKey,
        Guid uploadedBy,
        DateTimeOffset now,
        string? notes = null)
    {
        if (Status == DocumentStatus.Purged)
        {
            return Result.Failure<DocumentVersion>(DocumentErrors.Purged);
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Result.Failure<DocumentVersion>(DocumentErrors.FileNameRequired);
        }

        int number = CurrentVersionNumber + 1;

        var version = DocumentVersion.Create(
            Id, number, fileName, contentType, sizeInBytes, sha256, objectKey, uploadedBy, now, notes);

        _versions.Add(version);
        CurrentVersionNumber = number;
        UpdatedAt = now;

        // Uploading into a document somebody had asked to delete is a clear
        // statement that they want it after all, and leaving it scheduled for
        // purge would destroy the version they had just added.
        if (Status == DocumentStatus.MarkedForDeletion)
        {
            Status = DocumentStatus.Active;
            MarkedForDeletionAt = null;
            MarkedForDeletionBy = null;
            PurgeAfter = null;
        }

        return Result.Success(version);
    }

    public Result Rename(string title, string? category, DateTimeOffset now)
    {
        if (Status == DocumentStatus.Purged)
        {
            return Result.Failure(DocumentErrors.Purged);
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return Result.Failure(DocumentErrors.TitleRequired);
        }

        Title = title.Trim();
        Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        UpdatedAt = now;

        return Result.Success();
    }

    public Result Move(Guid? organizationUnitId, DateTimeOffset now)
    {
        if (Status == DocumentStatus.Purged)
        {
            return Result.Failure(DocumentErrors.Purged);
        }

        OrganizationUnitId = organizationUnitId;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// The first stage of deletion: the document leaves the listings and its
    /// content stays exactly where it is.
    /// <para>
    /// Two stages, because otherwise a misclick and a legal instruction look
    /// identical to the system and only one of them should be able to destroy
    /// something (§18.5). Inside the grace period the document can be restored;
    /// after it, only a backup can.
    /// </para>
    /// </summary>
    public Result MarkForDeletion(Guid actorUserId, TimeSpan gracePeriod, DateTimeOffset now)
    {
        if (Status == DocumentStatus.Purged)
        {
            return Result.Failure(DocumentErrors.Purged);
        }

        if (Status == DocumentStatus.MarkedForDeletion)
        {
            return Result.Failure(DocumentErrors.AlreadyMarkedForDeletion);
        }

        Status = DocumentStatus.MarkedForDeletion;
        MarkedForDeletionAt = now;
        MarkedForDeletionBy = actorUserId;
        PurgeAfter = now + gracePeriod;
        UpdatedAt = now;

        return Result.Success();
    }

    public Result Restore(DateTimeOffset now)
    {
        if (Status == DocumentStatus.Purged)
        {
            return Result.Failure(DocumentErrors.Purged);
        }

        if (Status != DocumentStatus.MarkedForDeletion)
        {
            return Result.Failure(DocumentErrors.NotMarkedForDeletion);
        }

        Status = DocumentStatus.Active;
        MarkedForDeletionAt = null;
        MarkedForDeletionBy = null;
        PurgeAfter = null;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// The second stage: the content is gone and the record is not.
    /// <para>
    /// The metadata row, its versions and its access history all survive, which
    /// is the point. "This document existed, these people read it, and it was
    /// destroyed on this date by this person" is the question asked after a
    /// deletion, and a row that was removed cannot answer it.
    /// </para>
    /// <para>
    /// It refuses to run early. The grace period is the only thing standing
    /// between a mistake and an irreversible one, and a caller able to waive it
    /// would eventually be called with the wrong argument.
    /// </para>
    /// </summary>
    public Result Purge(DateTimeOffset now)
    {
        if (Status == DocumentStatus.Purged)
        {
            return Result.Failure(DocumentErrors.Purged);
        }

        if (Status != DocumentStatus.MarkedForDeletion)
        {
            return Result.Failure(DocumentErrors.NotMarkedForDeletion);
        }

        if (PurgeAfter is { } due && now < due)
        {
            return Result.Failure(DocumentErrors.GracePeriodNotElapsed);
        }

        foreach (DocumentVersion version in _versions)
        {
            version.MarkContentRemoved(now);
        }

        Status = DocumentStatus.Purged;
        PurgedAt = now;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>The version bearing a number, or null.</summary>
    public DocumentVersion? VersionNumbered(int versionNumber) =>
        _versions.Find(v => v.VersionNumber == versionNumber);
}

/// <summary>Where a document is in its life.</summary>
public enum DocumentStatus
{
    /// <summary>Stored and readable.</summary>
    Active = 1,

    /// <summary>Deletion asked for; content intact and restorable.</summary>
    MarkedForDeletion = 2,

    /// <summary>Content destroyed; record and access history retained.</summary>
    Purged = 3
}
