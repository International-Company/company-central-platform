using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;

namespace CCP.Modules.Documents.Domain.Linking;

/// <summary>
/// A document attached to something in a business system.
/// <para>
/// <b>Deliberately polymorphic and deliberately unenforced.</b> The resource is
/// named by a type and an id that mean nothing here: a purchasing system links a
/// signed contract to <c>purchase-order</c> / <c>PO-2026-0041</c>, and the
/// Platform stores two strings.
/// </para>
/// <para>
/// A foreign key would be the other design, and it is the one that cannot exist.
/// The Platform is built before the business systems that use it and must
/// outlive any of them; a constraint pointing at <c>purchasing.orders</c> would
/// make the Documents module undeployable without the purchasing system and
/// undeletable with it. So the link is a fact the Platform records and the
/// business system is responsible for keeping true.
/// </para>
/// <para>
/// The consequence is honest and worth stating: a link can outlive the record it
/// points at. That shows as a link to a record the caller cannot resolve, which
/// is a great deal better than an entire module refusing to start.
/// </para>
/// </summary>
public sealed class DocumentLink : AggregateRoot, IAuditableEntity
{
    private DocumentLink() { }

    private DocumentLink(
        Guid id,
        Guid documentId,
        string resourceType,
        string resourceId,
        DateTimeOffset now)
        : base(id)
    {
        DocumentId = documentId;
        ResourceType = resourceType;
        ResourceId = resourceId;
        CreatedAt = now;
    }

    public Guid DocumentId { get; private set; }

    /// <summary>
    /// What kind of thing the document is attached to, in the calling system's
    /// own vocabulary. Normalised to lower case so <c>Purchase-Order</c> and
    /// <c>purchase-order</c> are not two different kinds of thing.
    /// </summary>
    public string ResourceType { get; private set; } = string.Empty;

    /// <summary>
    /// Which one. A string rather than a <c>Guid</c>, because a business system
    /// that keys its records by an invoice number is not doing anything wrong
    /// and should not have to invent an identifier to attach a file.
    /// </summary>
    public string ResourceId { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static Result<DocumentLink> Create(
        Guid documentId,
        string resourceType,
        string resourceId,
        DateTimeOffset now)
    {
        if (documentId == Guid.Empty)
        {
            return Result.Failure<DocumentLink>(DocumentErrors.NotFound);
        }

        if (string.IsNullOrWhiteSpace(resourceType))
        {
            return Result.Failure<DocumentLink>(DocumentErrors.ResourceTypeRequired);
        }

        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return Result.Failure<DocumentLink>(DocumentErrors.ResourceIdRequired);
        }

        return Result.Success(new DocumentLink(
            Uuid7.NewGuid(now),
            documentId,
            resourceType.Trim().ToLowerInvariant(),
            resourceId.Trim(),
            now));
    }
}
