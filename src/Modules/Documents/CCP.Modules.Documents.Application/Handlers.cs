using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Application.Configuration;
using CCP.Kernel.Paging;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Documents.Application.Abstractions;
using CCP.Modules.Documents.Application.Access;
using CCP.Modules.Documents.Application.Storage;
using CCP.Modules.Documents.Contracts.Dtos;
using CCP.Modules.Documents.Domain;
using CCP.Modules.Documents.Domain.Access;
using CCP.Modules.Documents.Domain.Auditing;
using CCP.Modules.Documents.Domain.Documents;
using CCP.Modules.Documents.Domain.Linking;

namespace CCP.Modules.Documents.Application;

// ---------------------------------------------------------------------------
// Commands and queries
// ---------------------------------------------------------------------------

/// <summary>A file arriving for the first time, with the document it creates.</summary>
public sealed record UploadDocumentCommand(
    string Title,
    string? Category,
    Guid? OrganizationUnitId,
    string FileName,
    string? ContentType,
    Stream Content);

/// <summary>A replacement for what is already there. The old version stays.</summary>
public sealed record AddVersionCommand(
    Guid DocumentId,
    string FileName,
    string? ContentType,
    Stream Content,
    string? Notes);

/// <summary>Changing what a document is called, or where it belongs.</summary>
public sealed record UpdateDocumentCommand(
    Guid DocumentId, string Title, string? Category, Guid? OrganizationUnitId);

public sealed record GetDocumentQuery(Guid DocumentId);

/// <summary>Finding documents, within what the caller may see.</summary>
public sealed record SearchDocumentsQuery(
    string? Term, string? Category, Guid? OrganizationUnitId, bool IncludeDeleted, PageRequest Page);

/// <summary>Asking for content, by version or by "the current one".</summary>
public sealed record DownloadDocumentQuery(Guid DocumentId, int? VersionNumber);

public sealed record GrantAccessCommand(
    Guid DocumentId,
    AccessSubjectKind SubjectKind,
    Guid SubjectId,
    DocumentAccessLevel Level,
    bool IncludesSubUnits);

public sealed record RevokeAccessCommand(Guid DocumentId, Guid RuleId);

public sealed record LinkDocumentCommand(Guid DocumentId, string ResourceType, string ResourceId);

public sealed record UnlinkDocumentCommand(Guid DocumentId, string ResourceType, string ResourceId);

/// <summary>What is attached to one business record.</summary>
public sealed record GetLinkedDocumentsQuery(string ResourceType, string ResourceId);

public sealed record GetAccessLogQuery(Guid DocumentId, PageRequest Page);

// ---------------------------------------------------------------------------
// Writing
// ---------------------------------------------------------------------------

/// <summary>
/// Stores a file and the document that holds it.
/// <para>
/// There is no access check here, and there is nothing missing: a document that
/// does not exist has no rules to check against. The permission to upload at all
/// is the endpoint's business, and the uploader becomes the owner — which is the
/// only sane default, since nobody else can yet have been given access.
/// </para>
/// </summary>
public sealed class UploadDocumentHandler(
    IDocumentRepository repository,
    DocumentContentService content,
    DocumentAccessGuard guard,
    ICurrentUser currentUser,
    IDocumentUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<DocumentDto>> HandleAsync(
        UploadDocumentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<DocumentDto>(DocumentErrors.AccessDenied);
        }

        DateTimeOffset now = clock.UtcNow;

        Result<Document> created = Document.Create(
            command.Title, command.Category, userId, command.OrganizationUnitId, now);

        if (created.IsFailure)
        {
            return Result.Failure<DocumentDto>(created.Error);
        }

        // The content goes through the pipeline before the row is added, so a
        // refused file leaves no empty document behind for somebody to wonder at.
        Result<DocumentContentService.StoredContent> stored = await content.AcceptAsync(
            command.Content, command.FileName, command.ContentType, cancellationToken);

        if (stored.IsFailure)
        {
            return Result.Failure<DocumentDto>(stored.Error);
        }

        Document document = created.Value;

        Result<DocumentVersion> version = document.AddVersion(
            command.FileName,
            stored.Value.ContentType,
            stored.Value.SizeInBytes,
            stored.Value.Sha256,
            stored.Value.ObjectKey,
            userId,
            now);

        if (version.IsFailure)
        {
            return Result.Failure<DocumentDto>(version.Error);
        }

        repository.Add(document);

        guard.RecordSuccess(
            document.Id, version.Value.VersionNumber, DocumentAction.Upload,
            $"{stored.Value.ContentType}, {stored.Value.SizeInBytes} bytes, scan: {stored.Value.ScanVerdict}");

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(DocumentMapper.ToDto(document, DocumentAccessLevel.Manage));
    }
}

/// <summary>Adds a version to a document that already exists.</summary>
public sealed class AddVersionHandler(
    DocumentContentService content,
    DocumentAccessGuard guard,
    ICurrentUser currentUser,
    IDocumentUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<DocumentDto>> HandleAsync(
        AddVersionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<DocumentAccessGuard.AuthorizedDocument> authorized = await guard.AuthorizeAsync(
            command.DocumentId, DocumentAccessLevel.Write, DocumentAction.AddVersion,
            cancellationToken: cancellationToken);

        if (authorized.IsFailure)
        {
            return Result.Failure<DocumentDto>(authorized.Error);
        }

        Document document = authorized.Value.Document;

        if (document.Status == DocumentStatus.Purged)
        {
            return Result.Failure<DocumentDto>(DocumentErrors.Purged);
        }

        Result<DocumentContentService.StoredContent> stored = await content.AcceptAsync(
            command.Content, command.FileName, command.ContentType, cancellationToken);

        if (stored.IsFailure)
        {
            await guard.RecordFailureAsync(
                document.Id, null, DocumentAction.AddVersion,
                stored.Error.Message, cancellationToken);

            return Result.Failure<DocumentDto>(stored.Error);
        }

        Result<DocumentVersion> version = document.AddVersion(
            command.FileName,
            stored.Value.ContentType,
            stored.Value.SizeInBytes,
            stored.Value.Sha256,
            stored.Value.ObjectKey,
            currentUser.UserId ?? Guid.Empty,
            clock.UtcNow,
            command.Notes);

        if (version.IsFailure)
        {
            return Result.Failure<DocumentDto>(version.Error);
        }

        guard.RecordSuccess(
            document.Id, version.Value.VersionNumber, DocumentAction.AddVersion,
            $"scan: {stored.Value.ScanVerdict}");

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(DocumentMapper.ToDto(document, authorized.Value.Level));
    }
}

/// <summary>
/// Renames a document, and moves it if asked.
/// <para>
/// Moving needs more than renaming does. A title is cosmetic; the unit a
/// document sits in decides who a unit rule reaches, so changing it is a change
/// to who can see the document and is treated as one.
/// </para>
/// </summary>
public sealed class UpdateDocumentHandler(
    DocumentAccessGuard guard, IDocumentUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result<DocumentDto>> HandleAsync(
        UpdateDocumentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<DocumentAccessGuard.AuthorizedDocument> authorized = await guard.AuthorizeAsync(
            command.DocumentId, DocumentAccessLevel.Write, DocumentAction.Update,
            cancellationToken: cancellationToken);

        if (authorized.IsFailure)
        {
            return Result.Failure<DocumentDto>(authorized.Error);
        }

        Document document = authorized.Value.Document;
        DateTimeOffset now = clock.UtcNow;
        bool moving = document.OrganizationUnitId != command.OrganizationUnitId;

        if (moving && authorized.Value.Level < DocumentAccessLevel.Manage)
        {
            await guard.RecordFailureAsync(
                document.Id, null, DocumentAction.Update,
                "moving a document between units needs Manage", cancellationToken);

            return Result.Failure<DocumentDto>(DocumentErrors.AccessDenied);
        }

        Result renamed = document.Rename(command.Title, command.Category, now);

        if (renamed.IsFailure)
        {
            return Result.Failure<DocumentDto>(renamed.Error);
        }

        if (moving)
        {
            Result moved = document.Move(command.OrganizationUnitId, now);

            if (moved.IsFailure)
            {
                return Result.Failure<DocumentDto>(moved.Error);
            }
        }

        guard.RecordSuccess(document.Id, null, DocumentAction.Update);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(DocumentMapper.ToDto(document, authorized.Value.Level));
    }
}

/// <summary>Asks for a document to be deleted. Content is untouched.</summary>
public sealed class DeleteDocumentHandler(
    DocumentAccessGuard guard,
    DocumentOptions options,
    IPlatformSettings settings,
    ICurrentUser currentUser,
    IDocumentUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        Guid documentId, CancellationToken cancellationToken = default)
    {
        Result<DocumentAccessGuard.AuthorizedDocument> authorized = await guard.AuthorizeAsync(
            documentId, DocumentAccessLevel.Manage, DocumentAction.MarkForDeletion,
            cancellationToken: cancellationToken);

        if (authorized.IsFailure)
        {
            return Result.Failure(authorized.Error);
        }

        // Read here, at the moment of deletion, and stamped onto the document as
        // an absolute instant. Shortening the grace period must not retroactively
        // destroy something already inside the one it was promised -- which is
        // exactly what reading it in the purge sweep instead would do.
        TimeSpan grace = await settings.GetDurationAsync(
            PlatformSettingKeys.DocumentDeletionGrace,
            options.DeletionGracePeriod,
            cancellationToken);

        Result marked = authorized.Value.Document.MarkForDeletion(
            currentUser.UserId ?? Guid.Empty, grace, clock.UtcNow);

        if (marked.IsFailure)
        {
            return Result.Failure(marked.Error);
        }

        guard.RecordSuccess(
            documentId, null, DocumentAction.MarkForDeletion,
            $"content may be destroyed after {authorized.Value.Document.PurgeAfter:u}");

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Takes back a deletion, inside the grace period.</summary>
public sealed class RestoreDocumentHandler(
    DocumentAccessGuard guard, IDocumentUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result> HandleAsync(
        Guid documentId, CancellationToken cancellationToken = default)
    {
        Result<DocumentAccessGuard.AuthorizedDocument> authorized = await guard.AuthorizeAsync(
            documentId, DocumentAccessLevel.Manage, DocumentAction.Restore,
            cancellationToken: cancellationToken);

        if (authorized.IsFailure)
        {
            return Result.Failure(authorized.Error);
        }

        Result restored = authorized.Value.Document.Restore(clock.UtcNow);

        if (restored.IsFailure)
        {
            return Result.Failure(restored.Error);
        }

        guard.RecordSuccess(documentId, null, DocumentAction.Restore);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

// ---------------------------------------------------------------------------
// Sharing
// ---------------------------------------------------------------------------

/// <summary>Gives somebody access, or changes what they have.</summary>
public sealed class GrantAccessHandler(
    IDocumentRepository repository,
    DocumentAccessGuard guard,
    IDocumentUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<DocumentAccessRuleDto>> HandleAsync(
        GrantAccessCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<DocumentAccessGuard.AuthorizedDocument> authorized = await guard.AuthorizeAsync(
            command.DocumentId, DocumentAccessLevel.Manage, DocumentAction.GrantAccess,
            cancellationToken: cancellationToken);

        if (authorized.IsFailure)
        {
            return Result.Failure<DocumentAccessRuleDto>(authorized.Error);
        }

        DateTimeOffset now = clock.UtcNow;

        // Granting again is how somebody changes a level, and refusing it would
        // make "give Sara write access" a two-step operation whose first step is
        // taking away the access she has.
        DocumentAccessRule? existing = authorized.Value.Rules.FirstOrDefault(
            r => r.SubjectKind == command.SubjectKind && r.SubjectId == command.SubjectId);

        if (existing is not null)
        {
            existing.ChangeLevel(command.Level, command.IncludesSubUnits, now);

            guard.RecordSuccess(
                command.DocumentId, null, DocumentAction.GrantAccess,
                $"{command.SubjectKind} {command.SubjectId} changed to {command.Level}");

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(DocumentMapper.ToDto(existing));
        }

        Result<DocumentAccessRule> rule = DocumentAccessRule.Create(
            command.DocumentId, command.SubjectKind, command.SubjectId,
            command.Level, command.IncludesSubUnits, now);

        if (rule.IsFailure)
        {
            return Result.Failure<DocumentAccessRuleDto>(rule.Error);
        }

        repository.AddRule(rule.Value);

        guard.RecordSuccess(
            command.DocumentId, null, DocumentAction.GrantAccess,
            $"{command.SubjectKind} {command.SubjectId} granted {command.Level}");

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(DocumentMapper.ToDto(rule.Value));
    }
}

/// <summary>Takes access away.</summary>
public sealed class RevokeAccessHandler(
    IDocumentRepository repository, DocumentAccessGuard guard, IDocumentUnitOfWork unitOfWork)
{
    public async Task<Result> HandleAsync(
        RevokeAccessCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<DocumentAccessGuard.AuthorizedDocument> authorized = await guard.AuthorizeAsync(
            command.DocumentId, DocumentAccessLevel.Manage, DocumentAction.RevokeAccess,
            cancellationToken: cancellationToken);

        if (authorized.IsFailure)
        {
            return Result.Failure(authorized.Error);
        }

        DocumentAccessRule? rule = await repository.GetRuleAsync(command.RuleId, cancellationToken);

        if (rule is null || rule.DocumentId != command.DocumentId)
        {
            return Result.Failure(DocumentErrors.AccessRuleNotFound);
        }

        repository.RemoveRule(rule);

        guard.RecordSuccess(
            command.DocumentId, null, DocumentAction.RevokeAccess,
            $"{rule.SubjectKind} {rule.SubjectId} no longer has {rule.Level}");

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Who has access, for somebody who already does.</summary>
public sealed class GetAccessRulesHandler(DocumentAccessGuard guard)
{
    public async Task<Result<IReadOnlyList<DocumentAccessRuleDto>>> HandleAsync(
        Guid documentId, CancellationToken cancellationToken = default)
    {
        Result<DocumentAccessGuard.AuthorizedDocument> authorized = await guard.AuthorizeAsync(
            documentId, DocumentAccessLevel.Read, DocumentAction.View,
            cancellationToken: cancellationToken);

        return authorized.IsFailure
            ? Result.Failure<IReadOnlyList<DocumentAccessRuleDto>>(authorized.Error)
            : Result.Success<IReadOnlyList<DocumentAccessRuleDto>>(
                [.. authorized.Value.Rules.Select(DocumentMapper.ToDto)]);
    }
}

// ---------------------------------------------------------------------------
// Linking
// ---------------------------------------------------------------------------

/// <summary>Attaches a document to a record in some other system.</summary>
public sealed class LinkDocumentHandler(
    IDocumentRepository repository,
    DocumentAccessGuard guard,
    IDocumentUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<DocumentLinkDto>> HandleAsync(
        LinkDocumentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<DocumentAccessGuard.AuthorizedDocument> authorized = await guard.AuthorizeAsync(
            command.DocumentId, DocumentAccessLevel.Write, DocumentAction.Link,
            cancellationToken: cancellationToken);

        if (authorized.IsFailure)
        {
            return Result.Failure<DocumentLinkDto>(authorized.Error);
        }

        Result<DocumentLink> link = DocumentLink.Create(
            command.DocumentId, command.ResourceType, command.ResourceId, clock.UtcNow);

        if (link.IsFailure)
        {
            return Result.Failure<DocumentLinkDto>(link.Error);
        }

        DocumentLink? existing = await repository.GetLinkAsync(
            command.DocumentId, link.Value.ResourceType, link.Value.ResourceId, cancellationToken);

        if (existing is not null)
        {
            return Result.Failure<DocumentLinkDto>(DocumentErrors.LinkExists);
        }

        repository.AddLink(link.Value);

        guard.RecordSuccess(
            command.DocumentId, null, DocumentAction.Link,
            $"{link.Value.ResourceType}/{link.Value.ResourceId}");

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(DocumentMapper.ToDto(link.Value));
    }
}

/// <summary>Detaches it again.</summary>
public sealed class UnlinkDocumentHandler(
    IDocumentRepository repository, DocumentAccessGuard guard, IDocumentUnitOfWork unitOfWork)
{
    public async Task<Result> HandleAsync(
        UnlinkDocumentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<DocumentAccessGuard.AuthorizedDocument> authorized = await guard.AuthorizeAsync(
            command.DocumentId, DocumentAccessLevel.Write, DocumentAction.Unlink,
            cancellationToken: cancellationToken);

        if (authorized.IsFailure)
        {
            return Result.Failure(authorized.Error);
        }

        DocumentLink? link = await repository.GetLinkAsync(
            command.DocumentId,
            command.ResourceType.Trim().ToLowerInvariant(),
            command.ResourceId.Trim(),
            cancellationToken);

        if (link is null)
        {
            return Result.Failure(DocumentErrors.LinkNotFound);
        }

        repository.RemoveLink(link);

        guard.RecordSuccess(
            command.DocumentId, null, DocumentAction.Unlink,
            $"{link.ResourceType}/{link.ResourceId}");

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>What is attached to one document.</summary>
public sealed class GetLinksHandler(IDocumentRepository repository, DocumentAccessGuard guard)
{
    public async Task<Result<IReadOnlyList<DocumentLinkDto>>> HandleAsync(
        Guid documentId, CancellationToken cancellationToken = default)
    {
        Result<DocumentAccessGuard.AuthorizedDocument> authorized = await guard.AuthorizeAsync(
            documentId, DocumentAccessLevel.Read, DocumentAction.View,
            cancellationToken: cancellationToken);

        if (authorized.IsFailure)
        {
            return Result.Failure<IReadOnlyList<DocumentLinkDto>>(authorized.Error);
        }

        IReadOnlyList<DocumentLink> links = await repository.GetLinksAsync(documentId, cancellationToken);

        return Result.Success<IReadOnlyList<DocumentLinkDto>>(
            [.. links.Select(DocumentMapper.ToDto)]);
    }
}

/// <summary>
/// The documents attached to one business record.
/// <para>
/// This is the query a business system actually makes — "what is filed against
/// purchase order 41?" — and it is filtered by what the caller may see rather
/// than by what is attached. Two people opening the same order can correctly see
/// different numbers of documents.
/// </para>
/// </summary>
public sealed class GetLinkedDocumentsHandler(
    IDocumentRepository repository,
    IAccessSubjectResolver subjects,
    ICallerScope callerScope,
    ICurrentUser currentUser)
{
    public async Task<Result<IReadOnlyList<DocumentDto>>> HandleAsync(
        GetLinkedDocumentsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<IReadOnlyList<DocumentDto>>(DocumentErrors.AccessDenied);
        }

        if (string.IsNullOrWhiteSpace(query.ResourceType))
        {
            return Result.Failure<IReadOnlyList<DocumentDto>>(DocumentErrors.ResourceTypeRequired);
        }

        if (string.IsNullOrWhiteSpace(query.ResourceId))
        {
            return Result.Failure<IReadOnlyList<DocumentDto>>(DocumentErrors.ResourceIdRequired);
        }

        IReadOnlyList<Document> documents = await repository.GetLinkedDocumentsAsync(
            query.ResourceType.Trim().ToLowerInvariant(),
            query.ResourceId.Trim(),
            cancellationToken);

        AccessSubject caller = await subjects.ResolveAsync(userId, cancellationToken);

        IReadOnlyDictionary<Guid, IReadOnlyList<DocumentAccessRule>> rulesByDocument =
            await repository.GetRulesForAsync([.. documents.Select(d => d.Id)], cancellationToken);

        List<DocumentDto> visible = [];

        foreach (Document document in documents)
        {
            IReadOnlyList<DocumentAccessRule> rules =
                rulesByDocument.TryGetValue(document.Id, out IReadOnlyList<DocumentAccessRule>? found)
                    ? found
                    : [];

            DocumentAccessLevel? level = DocumentAccessEvaluator.Evaluate(
                document, rules, caller, callerScope.ReachesWholeCompany);

            if (level is { } held)
            {
                visible.Add(DocumentMapper.ToDto(document, held, includeVersions: false));
            }
        }

        return Result.Success<IReadOnlyList<DocumentDto>>(visible);
    }
}

// ---------------------------------------------------------------------------
// Reading
// ---------------------------------------------------------------------------

/// <summary>One document, with its history of versions.</summary>
public sealed class GetDocumentHandler(DocumentAccessGuard guard, IDocumentUnitOfWork unitOfWork)
{
    public async Task<Result<DocumentDto>> HandleAsync(
        GetDocumentQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result<DocumentAccessGuard.AuthorizedDocument> authorized = await guard.AuthorizeAsync(
            query.DocumentId, DocumentAccessLevel.Read, DocumentAction.View,
            cancellationToken: cancellationToken);

        if (authorized.IsFailure)
        {
            return Result.Failure<DocumentDto>(authorized.Error);
        }

        guard.RecordSuccess(query.DocumentId, null, DocumentAction.View);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(
            DocumentMapper.ToDto(authorized.Value.Document, authorized.Value.Level));
    }
}

/// <summary>
/// Finding documents.
/// <para>
/// The filtering is done in the query and not afterwards. Fetching a page and
/// then removing what the caller may not see returns short pages and a total
/// that is a lie, and the page after it silently skips documents.
/// </para>
/// </summary>
public sealed class SearchDocumentsHandler(
    IDocumentRepository repository,
    IAccessSubjectResolver subjects,
    ICallerScope callerScope,
    ICurrentUser currentUser)
{
    public async Task<Result<PagedResult<DocumentDto>>> HandleAsync(
        SearchDocumentsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<PagedResult<DocumentDto>>(DocumentErrors.AccessDenied);
        }

        AccessSubject caller = await subjects.ResolveAsync(userId, cancellationToken);

        (IReadOnlyList<Document> items, long total) = await repository.SearchAsync(
            new DocumentSearch(
                query.Term,
                query.Category,
                query.OrganizationUnitId,
                query.IncludeDeleted,
                caller,
                callerScope.ReachesWholeCompany,
                query.Page),
            cancellationToken);

        IReadOnlyDictionary<Guid, IReadOnlyList<DocumentAccessRule>> rulesByDocument =
            await repository.GetRulesForAsync([.. items.Select(d => d.Id)], cancellationToken);

        List<DocumentDto> dtos = [];

        foreach (Document document in items)
        {
            IReadOnlyList<DocumentAccessRule> rules =
                rulesByDocument.TryGetValue(document.Id, out IReadOnlyList<DocumentAccessRule>? found)
                    ? found
                    : [];

            // The query has already limited this to documents the caller may
            // see, so a null here would mean the query and the evaluator
            // disagree. Read is the floor: the caller got the row, so they can
            // at least read it.
            DocumentAccessLevel level = DocumentAccessEvaluator.Evaluate(
                document, rules, caller, callerScope.ReachesWholeCompany)
                ?? DocumentAccessLevel.Read;

            dtos.Add(DocumentMapper.ToDto(document, level, includeVersions: false));
        }

        return Result.Success(new PagedResult<DocumentDto>(
            dtos, query.Page.Page, query.Page.PageSize, total));
    }
}

/// <summary>
/// Works out how the caller should fetch the content, and records that they did.
/// <para>
/// The download is logged here, when the answer is a pre-signed URL, because
/// that is the last moment the Platform is involved — the bytes are fetched from
/// the store directly and nothing else will ever know it happened. When there is
/// no URL the streaming endpoint does the logging instead, at the point the
/// bytes actually leave.
/// </para>
/// </summary>
public sealed class PrepareDownloadHandler(
    IDocumentStorageProvider storage,
    DocumentAccessGuard guard,
    DocumentOptions options,
    IDocumentUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<DocumentDownloadDto>> HandleAsync(
        DownloadDocumentQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result<DocumentVersion> resolved = await ResolveVersionAsync(query, guard, cancellationToken);

        if (resolved.IsFailure)
        {
            return Result.Failure<DocumentDownloadDto>(resolved.Error);
        }

        DocumentVersion version = resolved.Value;

        Uri? url = await storage.TryCreateReadUrlAsync(
            version.ObjectKey, version.FileName, version.ContentType,
            options.DownloadUrlLifetime, cancellationToken);

        if (url is not null)
        {
            guard.RecordSuccess(
                query.DocumentId, version.VersionNumber, DocumentAction.Download,
                $"pre-signed, valid {options.DownloadUrlLifetime.TotalMinutes:0} minutes");

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(new DocumentDownloadDto(
            version.FileName,
            version.ContentType,
            version.SizeInBytes,
            url?.ToString(),
            url is null ? null : clock.UtcNow + options.DownloadUrlLifetime));
    }

    /// <summary>
    /// The version being asked for, once the caller has been found entitled to it.
    /// <para>
    /// Shared with the streaming handler, because "which version, and may they
    /// have it, and does the content still exist" is three questions that must
    /// be answered identically by both paths.
    /// </para>
    /// </summary>
    internal static async Task<Result<DocumentVersion>> ResolveVersionAsync(
        DownloadDocumentQuery query,
        DocumentAccessGuard guard,
        CancellationToken cancellationToken)
    {
        Result<DocumentAccessGuard.AuthorizedDocument> authorized = await guard.AuthorizeAsync(
            query.DocumentId, DocumentAccessLevel.Read, DocumentAction.Download,
            query.VersionNumber, cancellationToken);

        if (authorized.IsFailure)
        {
            return Result.Failure<DocumentVersion>(authorized.Error);
        }

        Document document = authorized.Value.Document;

        DocumentVersion? version = query.VersionNumber is { } number
            ? document.VersionNumbered(number)
            : document.CurrentVersion;

        if (version is null)
        {
            return Result.Failure<DocumentVersion>(DocumentErrors.VersionNotFound);
        }

        if (version.ContentRemoved)
        {
            await guard.RecordFailureAsync(
                document.Id, version.VersionNumber, DocumentAction.Download,
                "the content has been purged", cancellationToken);

            return Result.Failure<DocumentVersion>(DocumentErrors.ContentUnavailable);
        }

        return Result.Success(version);
    }
}

/// <summary>
/// Hands over the bytes, through the Platform.
/// <para>
/// The fallback for a store that cannot issue pre-signed URLs, and the only path
/// in development. It is the slower one — a request thread is held for the
/// length of the download — and it is the one that always works.
/// </para>
/// </summary>
public sealed class OpenDocumentContentHandler(
    IDocumentStorageProvider storage, DocumentAccessGuard guard, IDocumentUnitOfWork unitOfWork)
{
    /// <summary>The content, and what to tell the browser about it.</summary>
    public sealed record OpenedContent(
        Stream Content, string FileName, string ContentType, long SizeInBytes);

    public async Task<Result<OpenedContent>> HandleAsync(
        DownloadDocumentQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result<DocumentVersion> resolved = await PrepareDownloadHandler.ResolveVersionAsync(
            query, guard, cancellationToken);

        if (resolved.IsFailure)
        {
            return Result.Failure<OpenedContent>(resolved.Error);
        }

        DocumentVersion version = resolved.Value;
        Stream? content = await storage.OpenAsync(version.ObjectKey, cancellationToken);

        if (content is null)
        {
            // The row says the content is there and the store disagrees. Worth
            // saying plainly rather than returning an empty file: this is the
            // shape of a database restored from a backup newer than the bucket.
            await guard.RecordFailureAsync(
                query.DocumentId, version.VersionNumber, DocumentAction.Download,
                "the stored object is missing", cancellationToken);

            return Result.Failure<OpenedContent>(DocumentErrors.ContentUnavailable);
        }

        guard.RecordSuccess(
            query.DocumentId, version.VersionNumber, DocumentAction.Download, "streamed");

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OpenedContent(
            content, version.FileName, version.ContentType, version.SizeInBytes));
    }
}

/// <summary>
/// Who has touched this document, including who tried and could not.
/// <para>
/// Reading it needs Manage. The log is a list of names against times, and a
/// document shared with forty people would otherwise tell each of them what the
/// other thirty-nine had been reading.
/// </para>
/// </summary>
public sealed class GetAccessLogHandler(IDocumentRepository repository, DocumentAccessGuard guard)
{
    public async Task<Result<PagedResult<DocumentAccessLogDto>>> HandleAsync(
        GetAccessLogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result<DocumentAccessGuard.AuthorizedDocument> authorized = await guard.AuthorizeAsync(
            query.DocumentId, DocumentAccessLevel.Manage, DocumentAction.View,
            cancellationToken: cancellationToken);

        if (authorized.IsFailure)
        {
            return Result.Failure<PagedResult<DocumentAccessLogDto>>(authorized.Error);
        }

        (IReadOnlyList<DocumentAccessLog> items, long total) = await repository.GetAccessLogAsync(
            query.DocumentId, query.Page.Skip, query.Page.PageSize, cancellationToken);

        return Result.Success(new PagedResult<DocumentAccessLogDto>(
            [.. items.Select(DocumentMapper.ToDto)],
            query.Page.Page, query.Page.PageSize, total));
    }
}
