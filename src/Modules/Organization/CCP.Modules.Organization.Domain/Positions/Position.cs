using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;

namespace CCP.Modules.Organization.Domain.Positions;

/// <summary>
/// A job position — "Finance Manager", "Systems Analyst".
/// <para>
/// <b>A position is not a permission role.</b> The distinction matters and is
/// easy to blur. A position describes what someone <i>does</i> in the
/// organization; a role (Phase 4) describes what they may <i>do</i> in the
/// software. They are related in practice and must stay separate in the model:
/// merging them would mean every job title change silently altering access, and
/// every permission grant needing an HR justification.
/// </para>
/// <para>
/// Workflow does use positions — "route this to whoever holds the Finance
/// Manager position" is an organizational routing rule, not a business one
/// (ARCHITECTURE.md §16.3). That is exactly the kind of use this type exists
/// for.
/// </para>
/// </summary>
public sealed class Position : AggregateRoot, IAuditableEntity
{
    private Position() { }

    private Position(Guid id, Guid companyId, string code, LocalizedName title, DateTimeOffset now)
        : base(id)
    {
        CompanyId = companyId;
        Code = code;
        Title = title;
        IsActive = true;
        CreatedAt = now;
    }

    public Guid CompanyId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public LocalizedName Title { get; private set; } = null!;

    /// <summary>
    /// Optional seniority hint, so an org chart can order positions sensibly.
    /// Deliberately not a permission level — see the type remarks.
    /// </summary>
    public int? Level { get; private set; }

    /// <summary>
    /// Positions are deactivated, not deleted: historical employee assignments
    /// still refer to them.
    /// </summary>
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static Result<Position> Create(
        Guid companyId,
        string code,
        LocalizedName title,
        int? level,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure<Position>(OrganizationErrors.CodeRequired);
        }

        string trimmed = code.Trim();

        if (trimmed.Length is < 2 or > 32)
        {
            return Result.Failure<Position>(OrganizationErrors.CodeLength);
        }

        return Result.Success(new Position(
            Uuid7.NewGuid(now), companyId, trimmed.ToUpperInvariant(), title, now)
        {
            Level = level
        });
    }

    public Result Rename(LocalizedName title, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(title);

        Title = title;
        UpdatedAt = now;

        return Result.Success();
    }

    public Result Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return Result.Failure(OrganizationErrors.UnitAlreadyInactive);
        }

        IsActive = false;
        UpdatedAt = now;

        return Result.Success();
    }

    public Result Reactivate(DateTimeOffset now)
    {
        if (IsActive)
        {
            return Result.Failure(OrganizationErrors.UnitAlreadyActive);
        }

        IsActive = true;
        UpdatedAt = now;

        return Result.Success();
    }
}
