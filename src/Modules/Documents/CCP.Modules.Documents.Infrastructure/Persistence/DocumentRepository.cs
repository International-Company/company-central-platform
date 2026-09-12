using CCP.Modules.Documents.Application.Abstractions;
using CCP.Modules.Documents.Domain.Access;
using CCP.Modules.Documents.Domain.Auditing;
using CCP.Modules.Documents.Domain.Documents;
using CCP.Modules.Documents.Domain.Linking;
using Microsoft.EntityFrameworkCore;

namespace CCP.Modules.Documents.Infrastructure.Persistence;

/// <summary>Reads and writes the documents schema.</summary>
public sealed class DocumentRepository(DocumentDbContext dbContext) : IDocumentRepository
{
    // --- Documents ----------------------------------------------------------

    public async Task<Document?> GetAsync(
        Guid documentId, CancellationToken cancellationToken = default)
        => await dbContext.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

    public void Add(Document document) => dbContext.Documents.Add(document);

    /// <summary>
    /// Documents matching the filters, limited to what the caller may see.
    /// <para>
    /// <b>The visibility predicate is part of the query, not a filter applied
    /// afterwards.</b> Filtering a fetched page produces short pages, a total
    /// that is wrong, and a second page that skips rows — and it means the
    /// database briefly handed the application documents it had no business
    /// reading.
    /// </para>
    /// </summary>
    public async Task<(IReadOnlyList<Document> Items, long Total)> SearchAsync(
        DocumentSearch search, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);

        IQueryable<Document> query = dbContext.Documents.AsNoTracking();

        query = search.IncludeDeleted
            ? query.Where(d => d.Status != DocumentStatus.Purged)
            : query.Where(d => d.Status == DocumentStatus.Active);

        if (!string.IsNullOrWhiteSpace(search.Term))
        {
            // ILIKE rather than ToLower().Contains(): the StringComparison
            // overload does not translate, and lowering both sides in SQL
            // discards any chance of using an index.
            string pattern = $"%{search.Term.Trim()}%";

            query = query.Where(d => EF.Functions.ILike(d.Title, pattern));
        }

        if (!string.IsNullOrWhiteSpace(search.Category))
        {
            string category = search.Category.Trim();

            query = query.Where(d => d.Category == category);
        }

        if (search.OrganizationUnitId is { } unit)
        {
            query = query.Where(d => d.OrganizationUnitId == unit);
        }

        if (!search.UnrestrictedByScope)
        {
            AccessSubject caller = search.Caller;
            Guid userId = caller.UserId;
            IReadOnlyList<Guid> roleIds = caller.RoleIds;
            Guid? unitId = caller.UnitId;
            IReadOnlyList<Guid> chain = caller.UnitChainIds;

            // The same four clauses as DocumentAccessEvaluator, expressed once
            // more because one of them has to run in SQL. They are checked
            // against each other by a test rather than by hope: a document that
            // appears here and cannot then be opened is the failure this pair
            // can produce.
            query = query.Where(d =>
                d.OwnerUserId == userId
                || dbContext.AccessRules.Any(r =>
                    r.DocumentId == d.Id
                    && ((r.SubjectKind == AccessSubjectKind.User && r.SubjectId == userId)
                        || (r.SubjectKind == AccessSubjectKind.Role && roleIds.Contains(r.SubjectId))
                        || (r.SubjectKind == AccessSubjectKind.OrganizationUnit
                            && (r.IncludesSubUnits
                                ? chain.Contains(r.SubjectId)
                                : unitId != null && r.SubjectId == unitId)))));
        }

        long total = await query.LongCountAsync(cancellationToken);

        List<Document> items = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip(search.Page.Skip)
            .Take(search.Page.PageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<IReadOnlyList<Document>> GetDuePurgesAsync(
        DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default)
        => await dbContext.Documents
            .Where(d => d.Status == DocumentStatus.MarkedForDeletion
                     && d.PurgeAfter != null
                     && d.PurgeAfter <= asOf)
            .OrderBy(d => d.PurgeAfter)
            .Take(limit)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Asked in batches, so the <c>IN</c> list stays a size PostgreSQL plans
    /// well and the sweep never holds the whole bucket in memory.
    /// </summary>
    public async Task<IReadOnlySet<string>> GetKnownObjectKeysAsync(
        IReadOnlyCollection<string> objectKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(objectKeys);

        if (objectKeys.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        // Versions, not documents. A key belongs to a version row, and a
        // document whose content has been purged still has its versions — which
        // is what keeps a purged document from reappearing as an orphan on every
        // pass for the rest of the Platform's life.
        List<string> known = await dbContext.Versions
            .AsNoTracking()
            .Where(version => objectKeys.Contains(version.ObjectKey))
            .Select(version => version.ObjectKey)
            .ToListAsync(cancellationToken);

        return known.ToHashSet(StringComparer.Ordinal);
    }

    // --- Access rules -------------------------------------------------------

    public async Task<IReadOnlyList<DocumentAccessRule>> GetRulesAsync(
        Guid documentId, CancellationToken cancellationToken = default)
        => await dbContext.AccessRules
            .Where(r => r.DocumentId == documentId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<DocumentAccessRule>>>
        GetRulesForAsync(
            IReadOnlyList<Guid> documentIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);

        if (documentIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<DocumentAccessRule>>();
        }

        List<DocumentAccessRule> rules = await dbContext.AccessRules
            .AsNoTracking()
            .Where(r => documentIds.Contains(r.DocumentId))
            .ToListAsync(cancellationToken);

        return rules
            .GroupBy(r => r.DocumentId)
            .ToDictionary(
                group => group.Key,
                IReadOnlyList<DocumentAccessRule> (group) => [.. group]);
    }

    public async Task<DocumentAccessRule?> GetRuleAsync(
        Guid ruleId, CancellationToken cancellationToken = default)
        => await dbContext.AccessRules
            .FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);

    public void AddRule(DocumentAccessRule rule) => dbContext.AccessRules.Add(rule);

    public void RemoveRule(DocumentAccessRule rule) => dbContext.AccessRules.Remove(rule);

    // --- Links --------------------------------------------------------------

    public async Task<IReadOnlyList<DocumentLink>> GetLinksAsync(
        Guid documentId, CancellationToken cancellationToken = default)
        => await dbContext.Links
            .AsNoTracking()
            .Where(l => l.DocumentId == documentId)
            .OrderBy(l => l.ResourceType)
            .ThenBy(l => l.ResourceId)
            .ToListAsync(cancellationToken);

    public async Task<DocumentLink?> GetLinkAsync(
        Guid documentId,
        string resourceType,
        string resourceId,
        CancellationToken cancellationToken = default)
        => await dbContext.Links
            .FirstOrDefaultAsync(
                l => l.DocumentId == documentId
                  && l.ResourceType == resourceType
                  && l.ResourceId == resourceId,
                cancellationToken);

    /// <summary>
    /// Everything filed against one business record.
    /// <para>
    /// Purged documents are excluded. Their record survives for the access log,
    /// and offering one on a purchase order screen would be offering a file that
    /// no longer exists.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<Document>> GetLinkedDocumentsAsync(
        string resourceType, string resourceId, CancellationToken cancellationToken = default)
        => await dbContext.Links
            .AsNoTracking()
            .Where(l => l.ResourceType == resourceType && l.ResourceId == resourceId)
            .Join(
                dbContext.Documents.AsNoTracking()
                    .Where(d => d.Status == DocumentStatus.Active),
                link => link.DocumentId,
                document => document.Id,
                (_, document) => document)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);

    public void AddLink(DocumentLink link) => dbContext.Links.Add(link);

    public void RemoveLink(DocumentLink link) => dbContext.Links.Remove(link);

    // --- Access log ---------------------------------------------------------

    public void AddAccessLog(DocumentAccessLog entry) => dbContext.AccessLog.Add(entry);

    public async Task<(IReadOnlyList<DocumentAccessLog> Items, long Total)> GetAccessLogAsync(
        Guid documentId, int skip, int take, CancellationToken cancellationToken = default)
    {
        IQueryable<DocumentAccessLog> query = dbContext.AccessLog
            .AsNoTracking()
            .Where(e => e.DocumentId == documentId);

        long total = await query.LongCountAsync(cancellationToken);

        List<DocumentAccessLog> items = await query
            .OrderByDescending(e => e.OccurredAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (items, total);
    }
}
