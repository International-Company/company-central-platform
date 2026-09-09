using CCP.Kernel.Paging;
using CCP.Kernel.Application.Auditing;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Organization.Application.Abstractions;
using CCP.Modules.Organization.Application.Units;
using CCP.Modules.Organization.Contracts.Dtos;
using CCP.Modules.Organization.Domain;
using CCP.Modules.Organization.Domain.Employees;
using CCP.Modules.Organization.Domain.Positions;
using CCP.Modules.Organization.Domain.Units;

namespace CCP.Modules.Organization.Application.Employees;

/// <summary>Creating an employee record.</summary>
public sealed record CreateEmployeeCommand(
    string EmployeeNumber,
    string FullNameAr,
    string FullNameEn,
    Guid UnitId,
    Guid? PositionId,
    Guid? ManagerId,
    Guid? UserId,
    string? WorkEmail,
    string? WorkPhone,
    DateOnly? HireDate);

public sealed class CreateEmployeeHandler(
    IOrganizationRepository repository,
    IOrganizationOutbox outbox,
    IOrganizationUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IClock clock)
{
    public async Task<Result<EmployeeDto>> HandleAsync(
        CreateEmployeeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        Domain.Companies.Company? company = await repository.GetSingleCompanyAsync(cancellationToken);

        if (company is null)
        {
            return Result.Failure<EmployeeDto>(OrganizationErrors.CompanyNotFound);
        }

        OrganizationUnit? unit = await repository.FindUnitAsync(command.UnitId, cancellationToken);

        if (unit is null)
        {
            return Result.Failure<EmployeeDto>(OrganizationErrors.UnitNotFound);
        }

        if (!unit.IsActive)
        {
            return Result.Failure<EmployeeDto>(OrganizationErrors.UnitInactive);
        }

        Position? position = null;

        if (command.PositionId is { } positionId)
        {
            position = await repository.FindPositionAsync(positionId, cancellationToken);

            if (position is null)
            {
                return Result.Failure<EmployeeDto>(OrganizationErrors.PositionNotFound);
            }

            if (!position.IsActive)
            {
                return Result.Failure<EmployeeDto>(OrganizationErrors.PositionInactive);
            }
        }

        Result<LocalizedName> fullName = LocalizedName.Create(command.FullNameAr, command.FullNameEn);

        if (fullName.IsFailure)
        {
            return Result.Failure<EmployeeDto>(fullName.Errors);
        }

        if (await repository.EmployeeNumberExistsAsync(
                company.Id, command.EmployeeNumber, cancellationToken))
        {
            return Result.Failure<EmployeeDto>(OrganizationErrors.EmployeeNumberTaken);
        }

        // One employee per user account. The unique partial index is the real
        // guarantee; this check exists for a clear message.
        if (command.UserId is { } userId
            && await repository.UserIsLinkedAsync(userId, null, cancellationToken))
        {
            return Result.Failure<EmployeeDto>(OrganizationErrors.UserAlreadyLinked);
        }

        Result<Employee> creation = Employee.Create(
            company.Id, command.EmployeeNumber, fullName.Value,
            command.UnitId, command.PositionId, command.HireDate, now);

        if (creation.IsFailure)
        {
            return Result.Failure<EmployeeDto>(creation.Errors);
        }

        Employee employee = creation.Value;

        if (command.ManagerId is { } managerId)
        {
            Result managerResult = await AssignManagerAsync(
                repository, employee, managerId, now, cancellationToken);

            if (managerResult.IsFailure)
            {
                return Result.Failure<EmployeeDto>(managerResult.Errors);
            }
        }

        employee.LinkUser(command.UserId, now);
        employee.UpdateContactDetails(command.WorkEmail, command.WorkPhone, now);

        repository.AddEmployee(employee);

        await CreateUnitHandler.PublishDomainEventsAsync(employee, outbox, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                "organization",
                "employee.created",
                AuditOutcome.Success,
                "employee",
                employee.Id.ToString(),
                NewValue: $$"""{"employeeNumber":"{{employee.EmployeeNumber}}","unit":"{{unit.Code}}"}"""),
            cancellationToken);

        return Result.Success(OrganizationMapper.ToDto(employee, unit.Code, position?.Code));
    }

    /// <summary>
    /// Assigns a manager after checking the reporting line for a loop.
    /// <para>
    /// Shared by creation and transfer, because both can set a manager and both
    /// must run the same check — a cycle introduced at creation is no less
    /// harmful than one introduced later.
    /// </para>
    /// </summary>
    internal static async Task<Result> AssignManagerAsync(
        IOrganizationRepository repository,
        Employee employee,
        Guid? managerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (managerId is { } id)
        {
            Employee? manager = await repository.FindEmployeeAsync(id, cancellationToken);

            if (manager is null)
            {
                return Result.Failure(OrganizationErrors.EmployeeNotFound);
            }

            Result cycleCheck = await ManagementChain.ValidateAssignmentAsync(
                employee.Id, id, repository.GetManagerIdAsync, cancellationToken);

            if (cycleCheck.IsFailure)
            {
                return cycleCheck;
            }
        }

        return employee.AssignManager(managerId, now);
    }
}

/// <summary>Moving an employee to a different unit, position or manager.</summary>
public sealed record TransferEmployeeCommand(
    Guid EmployeeId,
    Guid NewUnitId,
    Guid? NewPositionId,
    Guid? NewManagerId);

/// <summary>
/// Transfers an employee.
/// <para>
/// Raises <c>EmployeeTransferredEvent</c>, which from Phase 4 invalidates the
/// person's cached organizational scope — a <c>Unit</c> grant follows the
/// employee record, so a transfer changes what they can see.
/// </para>
/// </summary>
public sealed class TransferEmployeeHandler(
    IOrganizationRepository repository,
    IOrganizationOutbox outbox,
    IOrganizationUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        TransferEmployeeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        Employee? employee = await repository.FindEmployeeAsync(command.EmployeeId, cancellationToken);

        if (employee is null)
        {
            return Result.Failure(OrganizationErrors.EmployeeNotFound);
        }

        OrganizationUnit? unit = await repository.FindUnitAsync(command.NewUnitId, cancellationToken);

        if (unit is null)
        {
            return Result.Failure(OrganizationErrors.UnitNotFound);
        }

        if (!unit.IsActive)
        {
            return Result.Failure(OrganizationErrors.UnitInactive);
        }

        if (command.NewPositionId is { } positionId)
        {
            Position? position = await repository.FindPositionAsync(positionId, cancellationToken);

            if (position is null)
            {
                return Result.Failure(OrganizationErrors.PositionNotFound);
            }

            if (!position.IsActive)
            {
                return Result.Failure(OrganizationErrors.PositionInactive);
            }
        }

        Result managerResult = await CreateEmployeeHandler.AssignManagerAsync(
            repository, employee, command.NewManagerId, now, cancellationToken);

        if (managerResult.IsFailure)
        {
            return managerResult;
        }

        Result transfer = employee.Transfer(command.NewUnitId, command.NewPositionId, now);

        if (transfer.IsFailure)
        {
            return transfer;
        }

        await CreateUnitHandler.PublishDomainEventsAsync(employee, outbox, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                "organization",
                "employee.transferred",
                AuditOutcome.Success,
                "employee",
                command.EmployeeId.ToString(),
                NewValue: $$"""{"newUnitId":"{{command.NewUnitId}}","newPositionId":"{{command.NewPositionId}}"}"""),
            cancellationToken);

        return Result.Success();
    }
}

/// <summary>Linking or unlinking an employee's Platform account.</summary>
public sealed record LinkEmployeeUserCommand(Guid EmployeeId, Guid? UserId);

/// <summary>
/// Links an employee record to a Platform user account.
/// <para>
/// The account's existence is Identity's fact, not this module's, so it is
/// verified through Identity's contract rather than a foreign key
/// (ARCHITECTURE.md §6.3).
/// </para>
/// </summary>
public sealed class LinkEmployeeUserHandler(
    IOrganizationRepository repository,
    IOrganizationOutbox outbox,
    IOrganizationUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        LinkEmployeeUserCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Employee? employee = await repository.FindEmployeeAsync(command.EmployeeId, cancellationToken);

        if (employee is null)
        {
            return Result.Failure(OrganizationErrors.EmployeeNotFound);
        }

        if (command.UserId is { } userId
            && await repository.UserIsLinkedAsync(userId, employee.Id, cancellationToken))
        {
            return Result.Failure(OrganizationErrors.UserAlreadyLinked);
        }

        employee.LinkUser(command.UserId, clock.UtcNow);

        await CreateUnitHandler.PublishDomainEventsAsync(employee, outbox, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                "organization",
                "employee.user_linked",
                AuditOutcome.Success,
                "employee",
                command.EmployeeId.ToString(),
                NewValue: $$"""{"userId":"{{command.UserId}}"}"""),
            cancellationToken);

        return Result.Success();
    }
}

/// <summary>Searching employees.</summary>
/// <param name="ScopePathPrefixes">
/// The organizational paths the caller is permitted to reach, from their
/// authorization scope. Empty means unrestricted.
/// </param>
/// <param name="ScopeRestricted">
/// Whether a restriction applies at all. Distinguished from an empty prefix list
/// because "no restriction" and "restricted to nothing" must not look the same —
/// conflating them would either leak everything or return nothing.
/// </param>
public sealed record SearchEmployeesQuery(
    PageRequest Page,
    string? SearchTerm,
    Guid? UnitId,
    bool IncludeSubUnits,
    bool? IsActive,
    IReadOnlyList<string> ScopePathPrefixes,
    bool ScopeRestricted);

/// <summary>
/// Lists employees, optionally scoped to a unit and everything beneath it.
/// <para>
/// <c>IncludeSubUnits</c> is the same prefix-scan primitive authorization scope
/// will use from Phase 4 — proving here that "this unit and below" is one
/// indexed query rather than a tree walk.
/// </para>
/// </summary>
public sealed class SearchEmployeesHandler(IOrganizationRepository repository)
{
    /// <summary>Fields that are indexed and safe to sort by (ADR-008 §11.4).</summary>
    public static readonly IReadOnlySet<string> SortableFields =
        new HashSet<string>(StringComparer.Ordinal) { "employeeNumber", "nameEn", "nameAr", "createdAt" };

    public async Task<Result<PagedResult<EmployeeDto>>> HandleAsync(
        SearchEmployeesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result<SortSpec?> sort = SortSpec.Parse(query.Page.Sort, SortableFields);

        if (sort.IsFailure)
        {
            return Result.Failure<PagedResult<EmployeeDto>>(sort.Errors);
        }

        Domain.Companies.Company? company = await repository.GetSingleCompanyAsync(cancellationToken);

        if (company is null)
        {
            // An empty page, not a 404 — for the same reason the tree returns an
            // empty list. Nobody has been hired by a company that does not exist
            // yet, and saying "not found" turns a new installation into a broken
            // one on the screen.
            return Result.Success(new PagedResult<EmployeeDto>(
                [], query.Page.Page, query.Page.PageSize, 0));
        }

        // A restriction with no reachable paths returns nothing, rather than
        // falling through to unrestricted. That distinction is the difference
        // between an empty page and the whole company.
        if (query.ScopeRestricted && query.ScopePathPrefixes.Count == 0)
        {
            return Result.Success(new PagedResult<EmployeeDto>(
                [], query.Page.Page, query.Page.PageSize, 0));
        }

        (IReadOnlyList<Employee> items, long total) = await repository.SearchEmployeesAsync(
            company.Id,
            query.SearchTerm,
            query.UnitId,
            query.IncludeSubUnits,
            query.IsActive,
            query.Page.Skip,
            query.Page.PageSize,
            sort.Value?.Field,
            sort.Value?.Descending ?? false,
            query.ScopeRestricted ? query.ScopePathPrefixes : null,
            cancellationToken);

        // Unit and position codes are resolved in one pass rather than per row,
        // so a page of employees costs two extra queries, not two per employee.
        var unitCodes = new Dictionary<Guid, string>();
        var positionCodes = new Dictionary<Guid, string>();

        foreach (Employee employee in items)
        {
            if (!unitCodes.ContainsKey(employee.UnitId)
                && await repository.FindUnitAsync(employee.UnitId, cancellationToken) is { } unit)
            {
                unitCodes[employee.UnitId] = unit.Code;
            }

            if (employee.PositionId is { } positionId
                && !positionCodes.ContainsKey(positionId)
                && await repository.FindPositionAsync(positionId, cancellationToken) is { } position)
            {
                positionCodes[positionId] = position.Code;
            }
        }

        return Result.Success(new PagedResult<EmployeeDto>(
            [.. items.Select(e => OrganizationMapper.ToDto(
                e,
                unitCodes.GetValueOrDefault(e.UnitId, string.Empty),
                e.PositionId is { } pid ? positionCodes.GetValueOrDefault(pid) : null))],
            query.Page.Page,
            query.Page.PageSize,
            total));
    }
}
