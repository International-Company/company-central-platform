namespace CCP.Kernel.Domain;

/// <summary>
/// Timestamp and actor columns present on every mutable table
/// (ARCHITECTURE.md §10.3). Populated automatically by the persistence layer
/// so no handler has to remember.
/// </summary>
public interface IAuditableEntity
{
    DateTimeOffset CreatedAt { get; set; }

    Guid? CreatedBy { get; set; }

    DateTimeOffset? UpdatedAt { get; set; }

    Guid? UpdatedBy { get; set; }
}
