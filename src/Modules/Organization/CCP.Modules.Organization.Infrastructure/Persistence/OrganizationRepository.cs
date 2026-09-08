using CCP.Modules.Organization.Application.Abstractions;
using CCP.Modules.Organization.Domain.Companies;
using CCP.Modules.Organization.Domain.Employees;
using CCP.Modules.Organization.Domain.Positions;
using CCP.Modules.Organization.Domain.Units;
using Microsoft.EntityFrameworkCore;

namespace CCP.Modules.Organization.Infrastructure.Persistence;

/// <summary>EF Core implementation of the Organization module's persistence port.</summary>
public sealed class OrganizationRepository(OrganizationDbContext dbContext) : IOrganizationRepository
{
    // --- Company -----------------------------------------------------------

    public Task<Company?> FindCompanyAsync(Guid companyId, CancellationToken cancellationToken = default)
        => dbContext.Companies.FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);

    /// <summary>
    /// The company. The Platform is configured for one (ARCHITECTURE.md §27 Q2);
    /// if that changes, this is the method that stops making sense, which makes
    /// it a useful place for the assumption to live.
    /// </summary>
    public Task<Company?> GetSingleCompanyAsync(CancellationToken cancellationToken = default)
        => dbContext.Companies.OrderBy(c => c.CreatedAt).FirstOrDefaultAsync(cancellationToken);

    public Task<bool> AnyCompanyExistsAsync(CancellationToken cancellationToken = default)
        => dbContext.Companies.AnyAsync(cancellationToken);

    public void AddCompany(Company company) => dbContext.Companies.Add(company);

    // --- Units -------------------------------------------------------------

    public Task<OrganizationUnit?> FindUnitAsync(Guid unitId, CancellationToken cancellationToken = default)
        => dbContext.Units.FirstOrDefaultAsync(u => u.Id == unitId, cancellationToken);

    public Task<bool> UnitCodeExistsAsync(
        Guid companyId,
        string code,
        Guid? excludingUnitId = null,
        CancellationToken cancellationToken = default)
    {
        string normalized = code.Trim().ToUpperInvariant();

        return dbContext.Units.AnyAsync(
            u => u.CompanyId == companyId
                 && u.Code == normalized
                 && (excludingUnitId == null || u.Id != excludingUnitId),
            cancellationToken);
    }

    /// <summary>
    /// Everything beneath a unit, by prefix scan on the materialized path.
    /// <para>
    /// This is the query the whole path design exists for. <c>StartsWith</c>
    /// translates to <c>LIKE 'prefix%'</c>, which the <c>text_pattern_ops</c>
    /// index on <c>path</c> serves as a range scan. The alternative — a
    /// recursive CTE — would be a tree walk, and from Phase 4 this runs on every
    /// request that resolves an organizational scope.
    /// </para>
    /// <para>
    /// The unit itself is excluded: callers want its descendants, and including
    /// it would make every rebase loop try to rebase the moved unit onto itself.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<OrganizationUnit>> GetDescendantsAsync(
        OrganizationUnit unit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unit);

        return await dbContext.Units
            .Where(u => u.Id != unit.Id && u.Path.StartsWith(unit.Path))
            .OrderBy(u => u.Depth)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrganizationUnit>> GetUnitTreeAsync(
        Guid companyId,
        bool includeInactive,
        CancellationToken cancellationToken = default)
        => await dbContext.Units
            .AsNoTracking()
            .Where(u => u.CompanyId == companyId && (includeInactive || u.IsActive))
            .OrderBy(u => u.Depth)
            .ThenBy(u => u.SortOrder)
            .ThenBy(u => u.Code)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OrganizationUnit>> GetChildrenAsync(
        Guid parentId,
        CancellationToken cancellationToken = default)
        => await dbContext.Units
            .AsNoTracking()
            .Where(u => u.ParentId == parentId)
            .OrderBy(u => u.SortOrder)
            .ThenBy(u => u.Code)
            .ToListAsync(cancellationToken);

    public Task<bool> HasActiveChildrenAsync(Guid unitId, CancellationToken cancellationToken = default)
        => dbContext.Units.AnyAsync(u => u.ParentId == unitId && u.IsActive, cancellationToken);

    public void AddUnit(OrganizationUnit unit) => dbContext.Units.Add(unit);

    // --- Positions ---------------------------------------------------------

    public Task<Position?> FindPositionAsync(Guid positionId, CancellationToken cancellationToken = default)
        => dbContext.Positions.FirstOrDefaultAsync(p => p.Id == positionId, cancellationToken);

    public Task<bool> PositionCodeExistsAsync(
        Guid companyId,
        string code,
        CancellationToken cancellationToken = default)
    {
        string normalized = code.Trim().ToUpperInvariant();

        return dbContext.Positions.AnyAsync(
            p => p.CompanyId == companyId && p.Code == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<Position>> GetPositionsAsync(
        Guid companyId,
        bool includeInactive,
        CancellationToken cancellationToken = default)
        => await dbContext.Positions
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && (includeInactive || p.IsActive))
            .OrderBy(p => p.Level ?? int.MaxValue)
            .ThenBy(p => p.Code)
            .ToListAsync(cancellationToken);

    public void AddPosition(Position position) => dbContext.Positions.Add(position);

    // --- Employees ---------------------------------------------------------

    public Task<Employee?> FindEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => dbContext.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);

    public Task<Employee?> FindEmployeeByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => dbContext.Employees.FirstOrDefaultAsync(e => e.UserId == userId, cancellationToken);

    public Task<bool> EmployeeNumberExistsAsync(
        Guid companyId,
        string employeeNumber,
        CancellationToken cancellationToken = default)
    {
        string normalized = employeeNumber.Trim();

        return dbContext.Employees.AnyAsync(
            e => e.CompanyId == companyId && e.EmployeeNumber == normalized, cancellationToken);
    }

    public Task<bool> UserIsLinkedAsync(
        Guid userId,
        Guid? excludingEmployeeId = null,
        CancellationToken cancellationToken = default)
        => dbContext.Employees.AnyAsync(
            e => e.UserId == userId && (excludingEmployeeId == null || e.Id != excludingEmployeeId),
            cancellationToken);

    public Task<bool> HasActiveEmployeesAsync(Guid unitId, CancellationToken cancellationToken = default)
        => dbContext.Employees.AnyAsync(e => e.UnitId == unitId && e.IsActive, cancellationToken);

    /// <summary>
    /// One employee's manager id. Kept deliberately narrow — the cycle check
    /// calls it once per level, and loading whole entities to read one column
    /// would make a short walk expensive.
    /// </summary>
    public async Task<Guid?> GetManagerIdAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => e.ManagerId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<(IReadOnlyList<Employee> Items, long TotalCount)> SearchEmployeesAsync(
        Guid companyId,
        string? searchTerm,
        Guid? unitId,
        bool includeSubUnits,
        bool? isActive,
        int skip,
        int take,
        string? sortField,
        bool sortDescending,
        IReadOnlyList<string>? scopePathPrefixes,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Employee> query = dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId);

        // The authorization scope filter, applied at the data layer.
        //
        // This is the difference between deciding *whether* a caller may list
        // employees and deciding *which* employees they see. Applied here rather
        // than after loading, so a caller entitled to one department never has
        // the rest of the company in memory in the first place.
        if (scopePathPrefixes is { Count: > 0 })
        {
            // EF.Functions.Like rather than StartsWith: the StringComparison
            // overload the analyzer prefers is not translatable, and Like states
            // exactly the SQL the text_pattern_ops index on path serves.
            // Paths contain only hex and slashes, so no LIKE metacharacter can
            // appear in a prefix.
            IQueryable<Guid> reachableUnitIds = dbContext.Units
                .Where(u => scopePathPrefixes.Any(prefix => EF.Functions.Like(u.Path, prefix + "%")))
                .Select(u => u.Id);

            query = query.Where(e => reachableUnitIds.Contains(e.UnitId));
        }

        if (unitId is { } requiredUnitId)
        {
            if (includeSubUnits)
            {
                // "This unit and everything below it", as one prefix scan
                // against the unit table. The same primitive authorization
                // scope uses from Phase 4.
                OrganizationUnit? unit = await dbContext.Units
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == requiredUnitId, cancellationToken);

                if (unit is null)
                {
                    return ([], 0);
                }

                IQueryable<Guid> unitIds = dbContext.Units
                    .Where(u => u.Path.StartsWith(unit.Path))
                    .Select(u => u.Id);

                query = query.Where(e => unitIds.Contains(e.UnitId));
            }
            else
            {
                query = query.Where(e => e.UnitId == requiredUnitId);
            }
        }

        if (isActive is { } activeFilter)
        {
            query = query.Where(e => e.IsActive == activeFilter);
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            // ILIKE rather than ToLower().Contains(): the StringComparison
            // overloads are untranslatable and ToLower() applies a function to
            // every row.
            string pattern = $"%{EscapeLikePattern(searchTerm.Trim())}%";

            query = query.Where(e =>
                EF.Functions.ILike(e.EmployeeNumber, pattern)
                || EF.Functions.ILike(e.FullName.English, pattern)
                || EF.Functions.ILike(e.FullName.Arabic, pattern));
        }

        long total = await query.LongCountAsync(cancellationToken);

        query = (sortField, sortDescending) switch
        {
            ("employeeNumber", false) => query.OrderBy(e => e.EmployeeNumber),
            ("employeeNumber", true) => query.OrderByDescending(e => e.EmployeeNumber),
            ("nameEn", false) => query.OrderBy(e => e.FullName.English),
            ("nameEn", true) => query.OrderByDescending(e => e.FullName.English),
            ("nameAr", false) => query.OrderBy(e => e.FullName.Arabic),
            ("nameAr", true) => query.OrderByDescending(e => e.FullName.Arabic),
            (_, true) => query.OrderByDescending(e => e.CreatedAt),
            _ => query.OrderBy(e => e.CreatedAt)
        };

        List<Employee> items = await query.Skip(skip).Take(take).ToListAsync(cancellationToken);

        return (items, total);
    }

    public void AddEmployee(Employee employee) => dbContext.Employees.Add(employee);

    /// <summary>
    /// Escapes LIKE metacharacters in a search term. Not an injection concern —
    /// the value is parameterised — but an unescaped <c>%</c> matches everything
    /// and makes the database scan more than it should.
    /// </summary>
    private static string EscapeLikePattern(string term)
        => term.Replace("\\", "\\\\", StringComparison.Ordinal)
               .Replace("%", "\\%", StringComparison.Ordinal)
               .Replace("_", "\\_", StringComparison.Ordinal);
}
