namespace CCP.Modules.Documents.Contracts.Dtos;

/// <summary>
/// A document as somebody who may see it sees it.
/// <para>
/// The object key is deliberately absent. It is the one field that would let a
/// caller address the storage directly, and nothing outside the module has any
/// use for it.
/// </para>
/// </summary>
/// <param name="AccessLevel">What the caller may do with it, decided for this request.</param>
public sealed record DocumentDto(
    Guid Id,
    string Title,
    string? Category,
    Guid OwnerUserId,
    Guid? OrganizationUnitId,
    string Status,
    int CurrentVersionNumber,
    string? FileName,
    string? ContentType,
    long? SizeInBytes,
    string AccessLevel,
    DateTimeOffset? MarkedForDeletionAt,
    DateTimeOffset? PurgeAfter,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<DocumentVersionDto> Versions);

/// <summary>One stored set of bytes, and what is true about them.</summary>
public sealed record DocumentVersionDto(
    int VersionNumber,
    string FileName,
    string ContentType,
    long SizeInBytes,
    string Sha256,
    Guid UploadedBy,
    DateTimeOffset UploadedAt,
    string? Notes,
    bool ContentRemoved);

/// <summary>One statement of who may do what.</summary>
public sealed record DocumentAccessRuleDto(
    Guid Id,
    string SubjectKind,
    Guid SubjectId,
    string Level,
    bool IncludesSubUnits,
    DateTimeOffset CreatedAt);

/// <summary>A document attached to a record in some business system.</summary>
public sealed record DocumentLinkDto(
    Guid Id,
    Guid DocumentId,
    string ResourceType,
    string ResourceId,
    DateTimeOffset CreatedAt);

/// <summary>
/// One line of the access history, including the refusals.
/// </summary>
public sealed record DocumentAccessLogDto(
    Guid Id,
    int? VersionNumber,
    Guid ActorUserId,
    string Action,
    bool WasAllowed,
    string? Detail,
    string? IpAddress,
    DateTimeOffset OccurredAt);

/// <summary>
/// Where to fetch the content from.
/// <para>
/// Two shapes, one response. When the store can issue a pre-signed URL the
/// caller is sent to it directly and the bytes never pass through the Platform;
/// when it cannot, the caller streams from the Platform instead. The client
/// handles both by following whichever field is set, so the deployment can
/// change underneath it.
/// </para>
/// </summary>
/// <param name="Url">A short-lived direct URL, or null when the caller must stream.</param>
public sealed record DocumentDownloadDto(
    string FileName,
    string ContentType,
    long SizeInBytes,
    string? Url,
    DateTimeOffset? ExpiresAt);
