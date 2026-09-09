using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;

namespace CCP.Modules.Documents.Domain.Access;

/// <summary>
/// One statement of who may do what with one document.
/// <para>
/// <b>Permissions decide whether you may use documents at all; these decide
/// which ones.</b> Holding <c>platform.documents.read</c> and nothing else lets
/// somebody read the documents they own and the documents somebody has shared
/// with them — never every document in the company, which is what a permission
/// alone would mean.
/// </para>
/// <para>
/// The subject is a user, a role or an organizational unit, held as a bare id.
/// No foreign key leaves this schema (ADR-004), and the modules that own those
/// ids are reached through their public surfaces when a rule is evaluated.
/// </para>
/// </summary>
public sealed class DocumentAccessRule : AggregateRoot, IAuditableEntity
{
    private DocumentAccessRule() { }

    private DocumentAccessRule(
        Guid id,
        Guid documentId,
        AccessSubjectKind subjectKind,
        Guid subjectId,
        DocumentAccessLevel level,
        bool includesSubUnits,
        DateTimeOffset now)
        : base(id)
    {
        DocumentId = documentId;
        SubjectKind = subjectKind;
        SubjectId = subjectId;
        Level = level;
        IncludesSubUnits = includesSubUnits;
        CreatedAt = now;
    }

    public Guid DocumentId { get; private set; }

    public AccessSubjectKind SubjectKind { get; private set; }

    public Guid SubjectId { get; private set; }

    public DocumentAccessLevel Level { get; private set; }

    /// <summary>
    /// For a unit rule, whether it reaches the units beneath it.
    /// <para>
    /// Both readings are legitimate and neither is a safe default to assume: a
    /// policy shared with a whole division wants the subtree, and a document
    /// shared with the finance department specifically does not want every team
    /// under it. So it is asked rather than inferred.
    /// </para>
    /// <para>Meaningless, and always false, for user and role rules.</para>
    /// </summary>
    public bool IncludesSubUnits { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static Result<DocumentAccessRule> Create(
        Guid documentId,
        AccessSubjectKind subjectKind,
        Guid subjectId,
        DocumentAccessLevel level,
        bool includesSubUnits,
        DateTimeOffset now)
    {
        if (documentId == Guid.Empty)
        {
            return Result.Failure<DocumentAccessRule>(DocumentErrors.NotFound);
        }

        if (subjectId == Guid.Empty)
        {
            return Result.Failure<DocumentAccessRule>(DocumentErrors.SubjectRequired);
        }

        return Result.Success(new DocumentAccessRule(
            Uuid7.NewGuid(now),
            documentId,
            subjectKind,
            subjectId,
            level,
            subjectKind == AccessSubjectKind.OrganizationUnit && includesSubUnits,
            now));
    }

    public Result ChangeLevel(DocumentAccessLevel level, bool includesSubUnits, DateTimeOffset now)
    {
        Level = level;
        IncludesSubUnits = SubjectKind == AccessSubjectKind.OrganizationUnit && includesSubUnits;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>Whether this rule is at least as strong as what is being asked for.</summary>
    public bool Permits(DocumentAccessLevel required) => Level >= required;
}

/// <summary>Who a rule is about.</summary>
public enum AccessSubjectKind
{
    /// <summary>One named person.</summary>
    User = 1,

    /// <summary>Everybody holding a role.</summary>
    Role = 2,

    /// <summary>Everybody in a unit, and optionally beneath it.</summary>
    OrganizationUnit = 3
}

/// <summary>
/// How much a rule allows. Ordered, so a comparison is the whole check.
/// <para>
/// Three levels and not more. Every additional one is a distinction somebody
/// has to make correctly on a Tuesday afternoon while trying to share a file,
/// and the ones commonly proposed — "download but not view", "read but not
/// print" — are not enforceable by a system that hands over the bytes.
/// </para>
/// </summary>
public enum DocumentAccessLevel
{
    /// <summary>See the metadata and download the content.</summary>
    Read = 1,

    /// <summary>Read, and add a new version.</summary>
    Write = 2,

    /// <summary>Write, and change who else may do either, and delete it.</summary>
    Manage = 3
}
