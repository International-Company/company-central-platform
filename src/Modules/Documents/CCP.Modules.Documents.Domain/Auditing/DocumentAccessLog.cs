using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;

namespace CCP.Modules.Documents.Domain.Auditing;

/// <summary>
/// One person, one document, one moment, and what happened.
/// <para>
/// <b>Every access is logged, including the ones that were refused</b> (§18.3).
/// The refusals are the more interesting half: one person failing to open a
/// document is a wrong link, and one person failing to open forty is something
/// else, and a log that recorded only successes would show nothing in either
/// case.
/// </para>
/// <para>
/// This is separate from the Platform audit trail on purpose. The audit trail
/// records changes; this records <i>reads</i>, which are not changes and which
/// for documents are exactly what somebody will need to reconstruct later —
/// "who saw the salary letter" is a question about reads only.
/// </para>
/// <para>
/// Append-only. There is no method here that edits or removes a row, and the
/// row survives the purge of the content it describes.
/// </para>
/// </summary>
public sealed class DocumentAccessLog : Entity
{
    private DocumentAccessLog() { }

    private DocumentAccessLog(
        Guid id,
        Guid documentId,
        int? versionNumber,
        Guid actorUserId,
        DocumentAction action,
        bool wasAllowed,
        string? detail,
        string? ipAddress,
        string? userAgent,
        DateTimeOffset occurredAt)
        : base(id)
    {
        DocumentId = documentId;
        VersionNumber = versionNumber;
        ActorUserId = actorUserId;
        Action = action;
        WasAllowed = wasAllowed;
        Detail = detail;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        OccurredAt = occurredAt;
    }

    public Guid DocumentId { get; private set; }

    /// <summary>Which version, when the action was about one.</summary>
    public int? VersionNumber { get; private set; }

    public Guid ActorUserId { get; private set; }

    public DocumentAction Action { get; private set; }

    /// <summary>Whether it went through. False rows are attempts.</summary>
    public bool WasAllowed { get; private set; }

    /// <summary>Why it was refused, or anything else worth keeping.</summary>
    public string? Detail { get; private set; }

    /// <summary>
    /// Where the request came from, as the Platform saw it.
    /// <para>
    /// Nullable because a request that arrives through a proxy chain the
    /// Platform does not trust has no address it can honestly record, and an
    /// invented one is worse than none.
    /// </para>
    /// </summary>
    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public static DocumentAccessLog Allowed(
        Guid documentId,
        int? versionNumber,
        Guid actorUserId,
        DocumentAction action,
        string? ipAddress,
        string? userAgent,
        DateTimeOffset now,
        string? detail = null) =>
        new(Uuid7.NewGuid(now), documentId, versionNumber, actorUserId, action,
            true, detail, Trim(ipAddress), Trim(userAgent), now);

    public static DocumentAccessLog Denied(
        Guid documentId,
        int? versionNumber,
        Guid actorUserId,
        DocumentAction action,
        string reason,
        string? ipAddress,
        string? userAgent,
        DateTimeOffset now) =>
        new(Uuid7.NewGuid(now), documentId, versionNumber, actorUserId, action,
            false, reason, Trim(ipAddress), Trim(userAgent), now);

    /// <summary>
    /// A user agent is a header, and a header is whatever the client sent —
    /// including forty kilobytes of it. Truncated rather than refused, because
    /// losing the log entry would be the attacker getting what they wanted.
    /// </summary>
    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length > 400 ? value[..400] : value;
}

/// <summary>What was done, or attempted.</summary>
public enum DocumentAction
{
    /// <summary>Metadata was read.</summary>
    View = 1,

    /// <summary>Content was handed over.</summary>
    Download = 2,

    /// <summary>A first version was stored.</summary>
    Upload = 3,

    /// <summary>A further version was stored.</summary>
    AddVersion = 4,

    /// <summary>Title, category or unit changed.</summary>
    Update = 5,

    /// <summary>An access rule was added or changed.</summary>
    GrantAccess = 6,

    /// <summary>An access rule was removed.</summary>
    RevokeAccess = 7,

    /// <summary>Attached to a business record.</summary>
    Link = 8,

    /// <summary>Detached from one.</summary>
    Unlink = 9,

    /// <summary>Deletion asked for; content still there.</summary>
    MarkForDeletion = 10,

    /// <summary>Deletion taken back.</summary>
    Restore = 11,

    /// <summary>Content destroyed.</summary>
    Purge = 12
}
