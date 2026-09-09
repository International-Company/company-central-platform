using CCP.Modules.Organization.Contracts;
using CCP.Modules.Organization.Domain.Employees;
using CCP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CCP.Modules.Organization.Infrastructure;

/// <summary>
/// Implements the Organization module's public surface.
/// <para>
/// The only path by which another module reaches organizational data. Queries
/// here are narrow and read-only by design: <see cref="GetUnitPathForUserAsync"/>
/// in particular is called during permission resolution, so it selects one
/// column rather than materialising entities.
/// </para>
/// </summary>
public sealed class OrganizationDirectory(OrganizationDbContext dbContext) : IOrganizationDirectory
{
    public async Task<string?> GetUnitPathAsync(
        Guid unitId, CancellationToken cancellationToken = default)
        => await dbContext.Units
            .AsNoTracking()
            .Where(u => u.Id == unitId)
            .Select(u => u.Path)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Joins the employee to their unit and returns the path in one query.
    /// <para>
    /// On the permission-resolution path, so it avoids the obvious two-step —
    /// find the employee, then find their unit — which would double the cost of
    /// every scope resolution.
    /// </para>
    /// </summary>
    public async Task<string?> GetUnitPathForUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => await (
            from employee in dbContext.Employees.AsNoTracking()
            where employee.UserId == userId && employee.IsActive
            join unit in dbContext.Units.AsNoTracking() on employee.UnitId equals unit.Id
            select unit.Path)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Guid?> GetEmployeeIdForUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.IsActive)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Guid?> GetUserIdForEmployeeAsync(
        Guid employeeId, CancellationToken cancellationToken = default)
        => await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.Id == employeeId && e.IsActive)
            .Select(e => e.UserId)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Everyone holding a position who also has an account to act with.
    /// <para>
    /// The <c>UserId is not null</c> filter is the point: a task assigned to an
    /// employee with no account is a task nobody can ever open.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<Guid>> GetUserIdsInPositionAsync(
        Guid positionId, CancellationToken cancellationToken = default)
        => await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.PositionId == positionId && e.IsActive && e.UserId != null)
            .Select(e => e.UserId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The head of a unit: the active employee in it whose manager is not also
    /// in it.
    /// <para>
    /// Derived rather than stored. A stored head is a field that goes stale the
    /// first time somebody leaves and nobody remembers to update it — and the
    /// consequence of a stale one here is an approval sent to a person who no
    /// longer runs the department.
    /// </para>
    /// <para>
    /// If more than one qualifies the earliest-created is taken, so the answer
    /// is at least stable. A unit with two apparent heads is a reporting line
    /// that needs fixing, and picking arbitrarily would hide that by sending
    /// approvals to whichever the database happened to return.
    /// </para>
    /// </summary>
    public async Task<Guid?> GetUnitHeadUserIdAsync(
        Guid unitId, CancellationToken cancellationToken = default)
    {
        List<Guid> inUnit = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.UnitId == unitId && e.IsActive)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        if (inUnit.Count == 0)
        {
            return null;
        }

        return await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.UnitId == unitId
                     && e.IsActive
                     && e.UserId != null
                     && (e.ManagerId == null || !inUnit.Contains(e.ManagerId.Value)))
            .OrderBy(e => e.CreatedAt)
            .Select(e => e.UserId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<IReadOnlyList<Guid>> GetManagementChainAsync(
        Guid employeeId, CancellationToken cancellationToken = default)
        => ManagementChain.WalkUpAsync(employeeId, GetManagerIdAsync, cancellationToken);

    private async Task<Guid?> GetManagerIdAsync(Guid employeeId, CancellationToken cancellationToken)
        => await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => e.ManagerId)
            .FirstOrDefaultAsync(cancellationToken);
}
