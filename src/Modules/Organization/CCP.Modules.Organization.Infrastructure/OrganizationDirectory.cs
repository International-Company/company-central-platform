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
