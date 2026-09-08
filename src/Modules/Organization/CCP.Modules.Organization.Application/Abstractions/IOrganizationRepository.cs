using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Application.Events;
using CCP.Modules.Organization.Domain.Companies;
using CCP.Modules.Organization.Domain.Employees;
using CCP.Modules.Organization.Domain.Positions;
using CCP.Modules.Organization.Domain.Units;

namespace CCP.Modules.Organization.Application.Abstractions;

/// <summary>
/// The Organization module's unit of work. Module-scoped for the same reason as
/// Identity's: the container resolves by type, and a shared interface would let
/// one module's registration win for all of them.
/// </summary>
public interface IOrganizationUnitOfWork : IUnitOfWork;

/// <summary>The Organization module's outbox, bound to its own DbContext.</summary>
public interface IOrganizationOutbox : IOutbox;

/// <summary>
/// Persistence for the Organization module, expressed as the Application layer
/// needs it (ARCHITECTURE.md §6.1).
/// </summary>
public interface IOrganizationRepository
{
    // --- Company -----------------------------------------------------------

    Task<Company?> FindCompanyAsync(Guid companyId, CancellationToken cancellationToken = default);

    Task<Company?> GetSingleCompanyAsync(CancellationToken cancellationToken = default);

    Task<bool> AnyCompanyExistsAsync(CancellationToken cancellationToken = default);

    void AddCompany(Company company);

    // --- Units -------------------------------------------------------------

    Task<OrganizationUnit?> FindUnitAsync(Guid unitId, CancellationToken cancellationToken = default);

    Task<bool> UnitCodeExistsAsync(
        Guid companyId, string code, Guid? excludingUnitId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every unit beneath the given one, excluding it.
    /// <para>
    /// A prefix scan on the materialized path, not a recursive query. This is
    /// the operation a move has to rewrite, and the one authorization scope will
    /// depend on from Phase 4.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<OrganizationUnit>> GetDescendantsAsync(
        OrganizationUnit unit, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrganizationUnit>> GetUnitTreeAsync(
        Guid companyId, bool includeInactive, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrganizationUnit>> GetChildrenAsync(
        Guid parentId, CancellationToken cancellationToken = default);

    Task<bool> HasActiveChildrenAsync(Guid unitId, CancellationToken cancellationToken = default);

    void AddUnit(OrganizationUnit unit);

    // --- Positions ---------------------------------------------------------

    Task<Position?> FindPositionAsync(Guid positionId, CancellationToken cancellationToken = default);

    Task<bool> PositionCodeExistsAsync(
        Guid companyId, string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Position>> GetPositionsAsync(
        Guid companyId, bool includeInactive, CancellationToken cancellationToken = default);

    void AddPosition(Position position);

    // --- Employees ---------------------------------------------------------

    Task<Employee?> FindEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    Task<Employee?> FindEmployeeByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> EmployeeNumberExistsAsync(
        Guid companyId, string employeeNumber, CancellationToken cancellationToken = default);

    Task<bool> UserIsLinkedAsync(
        Guid userId, Guid? excludingEmployeeId = null, CancellationToken cancellationToken = default);

    Task<bool> HasActiveEmployeesAsync(Guid unitId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One employee's manager id, or null. Supplied to
    /// <c>ManagementChain</c> so the cycle check can walk the reporting line
    /// without the domain layer needing database access.
    /// </summary>
    Task<Guid?> GetManagerIdAsync(Guid employeeId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Employee> Items, long TotalCount)> SearchEmployeesAsync(
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
        CancellationToken cancellationToken = default);

    void AddEmployee(Employee employee);
}
