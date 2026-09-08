using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;

namespace CCP.Modules.Authorization.Domain.Permissions;

/// <summary>
/// One declared permission.
/// <para>
/// The Platform stores these but never interprets them. <c>finance.invoices.approve</c>
/// is, to this type, a validated string with an owner — the Financial system
/// knows what it means, and the Platform's job is only to record who holds it
/// and answer when asked (ADR-007 §14.2).
/// </para>
/// </summary>
public sealed class Permission : Entity, IAuditableEntity
{
    private Permission() { }

    private Permission(
        Guid id,
        Guid applicationId,
        PermissionName name,
        string description,
        DateTimeOffset now)
        : base(id)
    {
        ApplicationId = applicationId;
        Name = name.Value;
        Application = name.Application;
        Resource = name.Resource;
        Action = name.Action;
        Description = description;
        CreatedAt = now;
    }

    public Guid ApplicationId { get; private set; }

    /// <summary>The full name. Unique, and what everything compares against.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// The three parts, stored separately as well as combined.
    /// <para>
    /// Denormalized deliberately: the administration portal groups permissions
    /// by application and resource, and doing that by parsing the name on every
    /// row would be both slower and unindexable.
    /// </para>
    /// </summary>
    public string Application { get; private set; } = string.Empty;

    public string Resource { get; private set; } = string.Empty;

    public string Action { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    /// <summary>
    /// Whether the permission still exists in the declaring application.
    /// <para>
    /// A permission that disappears from an application's manifest is
    /// deactivated, not deleted: role assignments still reference it, and audit
    /// records from last year still name it.
    /// </para>
    /// </summary>
    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>
    /// Declares a permission for an application.
    /// <para>
    /// The name must sit inside the application's own namespace. Without that
    /// check, a registered business system could declare
    /// <c>platform.users.create</c> and grant itself the Platform's own
    /// administrative rights.
    /// </para>
    /// </summary>
    public static Result<Permission> Declare(
        Guid applicationId,
        string applicationCode,
        string permissionName,
        string? description,
        DateTimeOffset now)
    {
        Result<PermissionName> name = PermissionName.Parse(permissionName);

        if (name.IsFailure)
        {
            return Result.Failure<Permission>(name.Errors);
        }

        if (!name.Value.BelongsTo(applicationCode))
        {
            return Result.Failure<Permission>(
                AuthorizationErrors.PermissionOutsideApplicationNamespace(applicationCode));
        }

        return Result.Success(new Permission(
            Uuid7.NewGuid(now),
            applicationId,
            name.Value,
            string.IsNullOrWhiteSpace(description) ? name.Value.Value : description.Trim(),
            now));
    }

    public void UpdateDescription(string description, DateTimeOffset now)
    {
        Description = string.IsNullOrWhiteSpace(description) ? Name : description.Trim();
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    public void Reactivate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }
}
