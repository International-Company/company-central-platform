using CCP.Modules.Documents.Contracts.Dtos;
using CCP.Modules.Documents.Domain.Access;
using CCP.Modules.Documents.Domain.Auditing;
using CCP.Modules.Documents.Domain.Documents;
using CCP.Modules.Documents.Domain.Linking;

namespace CCP.Modules.Documents.Application;

/// <summary>
/// Turns the module's own types into the shapes it publishes.
/// <para>
/// Hand-written, and one direction only. The object key never appears in a DTO,
/// and a mapper that reflected over properties would put it there the moment
/// somebody added a field — which is the argument against the convenient kind.
/// </para>
/// </summary>
public static class DocumentMapper
{
    public static DocumentDto ToDto(
        Document document,
        DocumentAccessLevel accessLevel,
        bool includeVersions = true)
    {
        ArgumentNullException.ThrowIfNull(document);

        DocumentVersion? current = document.CurrentVersion;

        return new DocumentDto(
            document.Id,
            document.Title,
            document.Category,
            document.OwnerUserId,
            document.OrganizationUnitId,
            document.Status.ToString(),
            document.CurrentVersionNumber,
            current?.FileName,
            current?.ContentType,
            current?.SizeInBytes,
            accessLevel.ToString(),
            document.MarkedForDeletionAt,
            document.PurgeAfter,
            document.CreatedAt,
            document.UpdatedAt,
            includeVersions
                ? [.. document.Versions.OrderByDescending(v => v.VersionNumber).Select(ToDto)]
                : []);
    }

    public static DocumentVersionDto ToDto(DocumentVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new DocumentVersionDto(
            version.VersionNumber,
            version.FileName,
            version.ContentType,
            version.SizeInBytes,
            version.Sha256,
            version.UploadedBy,
            version.UploadedAt,
            version.Notes,
            version.ContentRemoved);
    }

    public static DocumentAccessRuleDto ToDto(DocumentAccessRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return new DocumentAccessRuleDto(
            rule.Id,
            rule.SubjectKind.ToString(),
            rule.SubjectId,
            rule.Level.ToString(),
            rule.IncludesSubUnits,
            rule.CreatedAt);
    }

    public static DocumentLinkDto ToDto(DocumentLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        return new DocumentLinkDto(
            link.Id, link.DocumentId, link.ResourceType, link.ResourceId, link.CreatedAt);
    }

    public static DocumentAccessLogDto ToDto(DocumentAccessLog entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new DocumentAccessLogDto(
            entry.Id,
            entry.VersionNumber,
            entry.ActorUserId,
            entry.Action.ToString(),
            entry.WasAllowed,
            entry.Detail,
            entry.IpAddress,
            entry.OccurredAt);
    }
}
