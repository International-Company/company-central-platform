using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;

namespace CCP.Modules.Documents.Domain.Documents;

/// <summary>
/// One set of bytes, and everything true about them at the moment they arrived.
/// <para>
/// A new upload to an existing document adds a row here; it never edits one.
/// The previous version keeps its object key and stays downloadable, which is
/// the whole of what versioning is for — somebody signed <i>that</i> copy, and
/// replacing it in place would leave the company with a signature attached to
/// text nobody has seen.
/// </para>
/// </summary>
public sealed class DocumentVersion : Entity
{
    private DocumentVersion() { }

    private DocumentVersion(
        Guid id,
        Guid documentId,
        int versionNumber,
        string fileName,
        string contentType,
        long sizeInBytes,
        string sha256,
        string objectKey,
        Guid uploadedBy,
        DateTimeOffset uploadedAt,
        string? notes)
        : base(id)
    {
        DocumentId = documentId;
        VersionNumber = versionNumber;
        FileName = fileName;
        ContentType = contentType;
        SizeInBytes = sizeInBytes;
        Sha256 = sha256;
        ObjectKey = objectKey;
        UploadedBy = uploadedBy;
        UploadedAt = uploadedAt;
        Notes = notes;
    }

    public Guid DocumentId { get; private set; }

    /// <summary>1 for the first upload, and one more for each after it.</summary>
    public int VersionNumber { get; private set; }

    /// <summary>
    /// The name the file had on the uploader's machine. <b>Metadata only.</b>
    /// <para>
    /// It is what the browser is told to save the file as, and it is never part
    /// of a storage key. A name is attacker-controlled text that may contain
    /// <c>../</c>, a null byte, a name that collides with somebody else's, or
    /// several thousand characters — see <see cref="ObjectKey"/> (§18.2).
    /// </para>
    /// </summary>
    public string FileName { get; private set; } = string.Empty;

    /// <summary>
    /// The type the <i>bytes</i> turned out to be, not the one the caller
    /// declared. Sent back on download, so a lie at upload cannot become a lie
    /// served to every later reader.
    /// </summary>
    public string ContentType { get; private set; } = string.Empty;

    public long SizeInBytes { get; private set; }

    /// <summary>
    /// A hex SHA-256 of the content as stored.
    /// <para>
    /// It answers "is this the file we were given?" years later, when the
    /// storage provider has been migrated twice and nobody remembers. It also
    /// makes an identical re-upload visible as an identical hash rather than as
    /// a second opaque version.
    /// </para>
    /// </summary>
    public string Sha256 { get; private set; } = string.Empty;

    /// <summary>
    /// Where the bytes actually are: a random key, generated here, related to
    /// nothing about the file.
    /// <para>
    /// Using the original name as a key invites path traversal and collisions
    /// (§18.2); using the document id makes keys guessable, which matters the
    /// day a bucket is misconfigured. This is a value that carries no
    /// information and can therefore leak none.
    /// </para>
    /// </summary>
    public string ObjectKey { get; private set; } = string.Empty;

    public Guid UploadedBy { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }

    /// <summary>Why this version exists, in the uploader's words. Optional.</summary>
    public string? Notes { get; private set; }

    /// <summary>Whether the bytes still exist. False after a purge.</summary>
    public bool ContentRemoved { get; private set; }

    public DateTimeOffset? ContentRemovedAt { get; private set; }

    internal static DocumentVersion Create(
        Guid documentId,
        int versionNumber,
        string fileName,
        string contentType,
        long sizeInBytes,
        string sha256,
        string objectKey,
        Guid uploadedBy,
        DateTimeOffset uploadedAt,
        string? notes) =>
        new(Uuid7.NewGuid(uploadedAt),
            documentId,
            versionNumber,
            fileName.Trim(),
            contentType,
            sizeInBytes,
            sha256,
            objectKey,
            uploadedBy,
            uploadedAt,
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());

    /// <summary>
    /// Record that the content is gone while keeping everything said about it.
    /// <para>
    /// The size, the hash and the file name stay. They are what makes a purged
    /// version an answer rather than an absence: "a 2.3 MB PDF named
    /// contract-final.pdf, with this hash, destroyed on this date".
    /// </para>
    /// </summary>
    internal void MarkContentRemoved(DateTimeOffset now)
    {
        ContentRemoved = true;
        ContentRemovedAt = now;
    }
}
