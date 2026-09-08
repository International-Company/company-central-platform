using CCP.Modules.Organization.Contracts.Dtos;
using CCP.Modules.Organization.Domain;
using CCP.Modules.Organization.Domain.Employees;
using CCP.Modules.Organization.Domain.Positions;
using CCP.Modules.Organization.Domain.Units;

namespace CCP.Modules.Organization.Application;

/// <summary>
/// Maps entities to their public shape.
/// <para>
/// Hand-written, and deliberately so: this is the boundary that decides what
/// leaves the module. An automatic mapper adds fields as entities gain them,
/// silently — which for <see cref="Employee"/> is exactly how HR business data
/// would end up in a Platform response (ADR-005).
/// </para>
/// </summary>
public static class OrganizationMapper
{
    public static LocalizedNameDto ToDto(LocalizedName name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return new LocalizedNameDto(name.Arabic, name.English);
    }

    public static OrganizationUnitDto ToDto(OrganizationUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);

        return new OrganizationUnitDto(
            unit.Id,
            unit.ParentId,
            unit.UnitType.ToString(),
            unit.Code,
            ToDto(unit.Name),
            unit.Depth,
            unit.SortOrder,
            unit.IsActive);
    }

    public static PositionDto ToDto(Position position)
    {
        ArgumentNullException.ThrowIfNull(position);

        return new PositionDto(
            position.Id,
            position.Code,
            ToDto(position.Title),
            position.Level,
            position.IsActive);
    }

    /// <summary>
    /// Maps an employee, resolving the unit and position codes the caller needs
    /// to display without a second request.
    /// </summary>
    public static EmployeeDto ToDto(Employee employee, string unitCode, string? positionCode)
    {
        ArgumentNullException.ThrowIfNull(employee);

        return new EmployeeDto(
            employee.Id,
            employee.EmployeeNumber,
            ToDto(employee.FullName),
            employee.UserId,
            employee.UnitId,
            unitCode,
            employee.PositionId,
            positionCode,
            employee.ManagerId,
            employee.WorkEmail,
            employee.WorkPhone,
            employee.HireDate,
            employee.IsActive);
    }

    /// <summary>
    /// Assembles a flat list of units into a tree.
    /// <para>
    /// One pass to index by id, one to attach each unit to its parent — linear,
    /// with no repeated scanning. Units whose parent is missing from the list
    /// (because it is inactive and was filtered out) are promoted to roots
    /// rather than dropped: silently losing a subtree from an org chart is worse
    /// than showing it slightly detached.
    /// </para>
    /// </summary>
    public static IReadOnlyList<OrganizationUnitTreeDto> BuildTree(IReadOnlyList<OrganizationUnit> units)
    {
        ArgumentNullException.ThrowIfNull(units);

        var childrenByParent = new Dictionary<Guid, List<OrganizationUnit>>();
        var present = new HashSet<Guid>(units.Count);
        var roots = new List<OrganizationUnit>();

        foreach (OrganizationUnit unit in units)
        {
            present.Add(unit.Id);
        }

        foreach (OrganizationUnit unit in units)
        {
            if (unit.ParentId is { } parentId && present.Contains(parentId))
            {
                if (!childrenByParent.TryGetValue(parentId, out List<OrganizationUnit>? siblings))
                {
                    siblings = [];
                    childrenByParent[parentId] = siblings;
                }

                siblings.Add(unit);
            }
            else
            {
                roots.Add(unit);
            }
        }

        return [.. roots
            .OrderBy(u => u.SortOrder)
            .ThenBy(u => u.Code, StringComparer.Ordinal)
            .Select(u => BuildNode(u, childrenByParent))];
    }

    private static OrganizationUnitTreeDto BuildNode(
        OrganizationUnit unit,
        Dictionary<Guid, List<OrganizationUnit>> childrenByParent)
    {
        List<OrganizationUnitTreeDto> children = childrenByParent.TryGetValue(
            unit.Id, out List<OrganizationUnit>? found)
            ? [.. found
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Code, StringComparer.Ordinal)
                .Select(c => BuildNode(c, childrenByParent))]
            : [];

        return new OrganizationUnitTreeDto(
            unit.Id,
            unit.ParentId,
            unit.UnitType.ToString(),
            unit.Code,
            ToDto(unit.Name),
            unit.Depth,
            unit.SortOrder,
            unit.IsActive,
            children);
    }
}
