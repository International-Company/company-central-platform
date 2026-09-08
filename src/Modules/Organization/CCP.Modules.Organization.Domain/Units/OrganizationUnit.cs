using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Organization.Domain.Units.Events;

namespace CCP.Modules.Organization.Domain.Units;

/// <summary>
/// One node of the company structure: a department, section, centre or any other
/// kind of unit (see <see cref="OrganizationUnitType"/>).
/// <para>
/// <b>Adjacency list plus materialized path.</b> <see cref="ParentId"/> gives
/// the immediate parent, which is what edits and validation need.
/// <see cref="Path"/> encodes the full ancestry as text, which turns "everything
/// under this unit" into a single indexed prefix scan instead of a recursive
/// query.
/// </para>
/// <para>
/// That second property is not a nicety. From Phase 4, authorization scope
/// (<c>Unit</c>, <c>UnitAndBelow</c>) is resolved on <b>every request</b>
/// (ADR-007 §14.3). A recursive CTE per request would put a tree walk on the hot
/// path of every call in the company. A prefix scan on an indexed text column
/// does not.
/// </para>
/// <para>
/// The cost is that a move must rewrite the path of every descendant, in one
/// transaction. That is a rare operation and a frequent read, so the trade is
/// firmly the right way round — but it is the reason
/// <see cref="MoveTo"/> and its descendant rewrite are treated carefully.
/// </para>
/// </summary>
public sealed class OrganizationUnit : AggregateRoot, IAuditableEntity
{
    /// <summary>Separates path segments. Never valid inside a segment.</summary>
    public const char PathSeparator = '/';

    private OrganizationUnit() { }

    private OrganizationUnit(
        Guid id,
        Guid companyId,
        Guid? parentId,
        OrganizationUnitType unitType,
        string code,
        LocalizedName name,
        string path,
        int depth,
        DateTimeOffset now)
        : base(id)
    {
        CompanyId = companyId;
        ParentId = parentId;
        UnitType = unitType;
        Code = code;
        Name = name;
        Path = path;
        Depth = depth;
        IsActive = true;
        CreatedAt = now;
    }

    public Guid CompanyId { get; private set; }

    /// <summary>The immediate parent. Null for a top-level unit.</summary>
    public Guid? ParentId { get; private set; }

    public OrganizationUnitType UnitType { get; private set; }

    /// <summary>
    /// A short human-readable identifier, unique within the company — the code
    /// people actually use when they talk about a department. Distinct from the
    /// id, which is stable but meaningless to a person.
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    public LocalizedName Name { get; private set; } = null!;

    /// <summary>
    /// The full ancestry, as <c>/{rootId}/{childId}/{thisId}/</c> with ids in
    /// 32-character hex form.
    /// <para>
    /// Leading and trailing separators are deliberate: they make a prefix match
    /// unambiguous. Without the trailing separator, the path of unit <c>ab</c>
    /// would prefix-match unit <c>abc</c>, and a scope check would silently
    /// include units it should not.
    /// </para>
    /// </summary>
    public string Path { get; private set; } = string.Empty;

    /// <summary>
    /// Distance from the root, zero-based. Derived from the path and stored
    /// because ordering a tree by depth is common and recomputing it per row is
    /// wasteful.
    /// </summary>
    public int Depth { get; private set; }

    /// <summary>
    /// Ordering hint among siblings, so an org chart can be presented the way
    /// the company thinks of itself rather than alphabetically.
    /// </summary>
    public int SortOrder { get; private set; }

    /// <summary>
    /// Whether the unit is in use. Units are deactivated, not deleted: an
    /// employee record, an audit entry or a business document from three years
    /// ago still refers to it, and deleting the row would orphan all of them.
    /// </summary>
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    // -----------------------------------------------------------------------
    // Creation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a unit under an optional parent.
    /// <para>
    /// The parent is passed as an entity rather than an id so the path can be
    /// derived from it. Passing an id would mean either trusting the caller to
    /// supply the path too, or a lookup the caller has usually already done.
    /// </para>
    /// </summary>
    public static Result<OrganizationUnit> Create(
        Guid companyId,
        OrganizationUnit? parent,
        OrganizationUnitType unitType,
        string code,
        LocalizedName name,
        DateTimeOffset now)
    {
        Result codeResult = ValidateCode(code);

        if (codeResult.IsFailure)
        {
            return Result.Failure<OrganizationUnit>(codeResult.Errors);
        }

        if (parent is not null && parent.CompanyId != companyId)
        {
            return Result.Failure<OrganizationUnit>(OrganizationErrors.ParentInDifferentCompany);
        }

        Guid id = Uuid7.NewGuid(now);
        string parentPath = parent?.Path ?? string.Empty;
        int depth = parent is null ? 0 : parent.Depth + 1;

        var unit = new OrganizationUnit(
            id,
            companyId,
            parent?.Id,
            unitType,
            code.Trim().ToUpperInvariant(),
            name,
            BuildPath(parentPath, id),
            depth,
            now);

        unit.Raise(new OrganizationUnitCreatedEvent(
            unit.Id, unit.Code, unit.Name.English, unit.ParentId, now));

        return unit;
    }

    // -----------------------------------------------------------------------
    // Hierarchy
    // -----------------------------------------------------------------------

    /// <summary>
    /// Whether <paramref name="candidate"/> is this unit or sits beneath it.
    /// <para>
    /// This is the primitive authorization scope is built on: a
    /// <c>UnitAndBelow</c> grant covers exactly the units for which this returns
    /// true.
    /// </para>
    /// </summary>
    public bool Contains(OrganizationUnit candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return candidate.Path.StartsWith(Path, StringComparison.Ordinal);
    }

    /// <summary>The ids of every ancestor, nearest last, excluding this unit.</summary>
    public IReadOnlyList<Guid> AncestorIds()
    {
        string[] segments = Path.Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        // The final segment is this unit.
        return [.. segments[..^1].Select(s => Guid.ParseExact(s, "N"))];
    }

    /// <summary>
    /// Moves this unit under a new parent, or to the top level when
    /// <paramref name="newParent"/> is null.
    /// <para>
    /// Returns the new path prefix so the caller can rewrite descendants. This
    /// method does <b>not</b> rewrite them: it cannot, because it does not have
    /// them loaded. The caller must, in the same transaction — see
    /// <see cref="RebaseUnder"/>.
    /// </para>
    /// </summary>
    public Result<PathChange> MoveTo(OrganizationUnit? newParent, DateTimeOffset now)
    {
        if (newParent is not null)
        {
            if (newParent.CompanyId != CompanyId)
            {
                return Result.Failure<PathChange>(OrganizationErrors.ParentInDifferentCompany);
            }

            if (newParent.Id == Id)
            {
                return Result.Failure<PathChange>(OrganizationErrors.UnitCannotBeItsOwnParent);
            }

            // The cycle check. Moving a unit beneath one of its own descendants
            // would detach that whole branch from the root and make the path
            // self-referential. The materialized path makes this a single
            // comparison rather than a tree walk.
            if (Contains(newParent))
            {
                return Result.Failure<PathChange>(OrganizationErrors.UnitCannotMoveUnderOwnDescendant);
            }
        }

        if (newParent?.Id == ParentId)
        {
            // Already there. Not an error — the caller asked for a state that
            // already holds.
            return Result.Success(new PathChange(Path, Path, Depth, Depth));
        }

        string oldPath = Path;
        int oldDepth = Depth;

        ParentId = newParent?.Id;
        Path = BuildPath(newParent?.Path ?? string.Empty, Id);
        Depth = newParent is null ? 0 : newParent.Depth + 1;
        UpdatedAt = now;

        Raise(new OrganizationUnitMovedEvent(Id, Code, ParentId, oldPath, Path, now));

        return Result.Success(new PathChange(oldPath, Path, oldDepth, Depth));
    }

    /// <summary>
    /// Rewrites this unit's path after an ancestor moved.
    /// <para>
    /// Applied by the move use case to every descendant of the moved unit.
    /// Replacing the old prefix rather than rebuilding from the parent means a
    /// descendant's own sub-structure is preserved without loading it.
    /// </para>
    /// </summary>
    public void RebaseUnder(PathChange change, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (!Path.StartsWith(change.OldPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unit {Id} is not beneath the moved unit and must not be rebased. "
                + $"Its path is '{Path}'; the moved unit's old path was '{change.OldPath}'.");
        }

        Path = string.Concat(change.NewPath, Path.AsSpan(change.OldPath.Length));
        Depth += change.NewDepth - change.OldDepth;
        UpdatedAt = now;
    }

    // -----------------------------------------------------------------------
    // Editing
    // -----------------------------------------------------------------------

    public Result Rename(LocalizedName name, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(name);

        Name = name;
        UpdatedAt = now;

        return Result.Success();
    }

    public void Reorder(int sortOrder, DateTimeOffset now)
    {
        SortOrder = sortOrder;
        UpdatedAt = now;
    }

    /// <summary>
    /// Deactivates the unit. The caller must first establish that it has no
    /// active children and no assigned employees — this type cannot see either.
    /// </summary>
    public Result Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return Result.Failure(OrganizationErrors.UnitAlreadyInactive);
        }

        IsActive = false;
        UpdatedAt = now;

        Raise(new OrganizationUnitDeactivatedEvent(Id, Code, now));

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

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds <c>{parentPath}{id}/</c>, with a leading separator at the root.
    /// The trailing separator is what makes prefix matching unambiguous.
    /// </summary>
    internal static string BuildPath(string parentPath, Guid id)
    {
        string prefix = string.IsNullOrEmpty(parentPath)
            ? PathSeparator.ToString()
            : parentPath;

        return $"{prefix}{id:N}{PathSeparator}";
    }

    private static Result ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure(OrganizationErrors.CodeRequired);
        }

        string trimmed = code.Trim();

        if (trimmed.Length is < 2 or > 32)
        {
            return Result.Failure(OrganizationErrors.CodeLength);
        }

        foreach (char c in trimmed)
        {
            // An allow-list. Codes appear in URLs, exports and audit records,
            // and the path separator must never occur in one.
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.'))
            {
                return Result.Failure(OrganizationErrors.CodeCharacters);
            }
        }

        return Result.Success();
    }
}

/// <summary>
/// The before and after of a move, so descendants can be rebased.
/// </summary>
/// <param name="OldPath">The moved unit's path before the move.</param>
/// <param name="NewPath">Its path after.</param>
/// <param name="OldDepth">Its depth before.</param>
/// <param name="NewDepth">Its depth after.</param>
public sealed record PathChange(string OldPath, string NewPath, int OldDepth, int NewDepth)
{
    /// <summary>True when the move did not actually change anything.</summary>
    public bool IsNoOp => string.Equals(OldPath, NewPath, StringComparison.Ordinal);
}
