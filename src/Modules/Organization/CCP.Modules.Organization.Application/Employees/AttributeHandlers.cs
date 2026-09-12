using CCP.Kernel.Application.Auditing;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Organization.Application.Abstractions;
using CCP.Modules.Organization.Contracts.Dtos;
using CCP.Modules.Organization.Domain;
using CCP.Modules.Organization.Domain.Employees;

namespace CCP.Modules.Organization.Application.Employees;

/// <summary>Setting one piece of an application's own metadata on a person.</summary>
public sealed record SetEmployeeAttributeCommand(Guid EmployeeId, string Key, string Value);

public sealed record RemoveEmployeeAttributeCommand(Guid EmployeeId, string Key);

public sealed record GetEmployeeAttributesQuery(Guid EmployeeId);

/// <summary>
/// Writes one custom attribute.
/// <para>
/// <b>Audited, and that is not bureaucracy.</b> The attribute bag is where a
/// business application keeps its own facts about a person, and "who decided
/// this employee was flagged" is a question somebody will eventually ask about
/// a row nobody remembers writing.
/// </para>
/// </summary>
public sealed class SetEmployeeAttributeHandler(
    IOrganizationRepository repository,
    IOrganizationUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IClock clock)
{
    private const string ModuleName = "organization";

    public async Task<Result> HandleAsync(
        SetEmployeeAttributeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Employee? employee =
            await repository.FindEmployeeAsync(command.EmployeeId, cancellationToken);

        if (employee is null)
        {
            return Result.Failure(OrganizationErrors.EmployeeNotFound);
        }

        Result set = employee.SetAttribute(command.Key, command.Value, clock.UtcNow);

        if (set.IsFailure)
        {
            return set;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The key, never the value. An attribute's value is an application's
        // business and may be somebody's medical category or salary band; the
        // trail records that it changed and who changed it, which is what the
        // question is actually about.
        await auditTrail.RecordAsync(
            new AuditEntry(
                ModuleName,
                "employee.attribute.set",
                AuditOutcome.Success,
                "employee",
                employee.Id.ToString(),
                Metadata: $$"""{"key":"{{command.Key}}"}"""),
            cancellationToken);

        return Result.Success();
    }
}

public sealed class RemoveEmployeeAttributeHandler(
    IOrganizationRepository repository,
    IOrganizationUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IClock clock)
{
    private const string ModuleName = "organization";

    public async Task<Result> HandleAsync(
        RemoveEmployeeAttributeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Employee? employee =
            await repository.FindEmployeeAsync(command.EmployeeId, cancellationToken);

        if (employee is null)
        {
            return Result.Failure(OrganizationErrors.EmployeeNotFound);
        }

        employee.RemoveAttribute(command.Key, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                ModuleName,
                "employee.attribute.removed",
                AuditOutcome.Success,
                "employee",
                employee.Id.ToString(),
                Metadata: $$"""{"key":"{{command.Key}}"}"""),
            cancellationToken);

        return Result.Success();
    }
}

/// <summary>
/// Everything kept about a person by everybody.
/// <para>
/// Returned whole rather than filtered to the calling application. <b>"What do
/// you hold about me" is a question a company must be able to answer in full</b>,
/// and an endpoint that only ever showed one application's slice would make the
/// complete answer something only a database query could produce.
/// </para>
/// </summary>
public sealed class GetEmployeeAttributesHandler(IOrganizationRepository repository)
{
    public async Task<Result<IReadOnlyList<EmployeeAttributeDto>>> HandleAsync(
        GetEmployeeAttributesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        Employee? employee =
            await repository.FindEmployeeAsync(query.EmployeeId, cancellationToken);

        if (employee is null)
        {
            return Result.Failure<IReadOnlyList<EmployeeAttributeDto>>(
                OrganizationErrors.EmployeeNotFound);
        }

        return Result.Success<IReadOnlyList<EmployeeAttributeDto>>(
            [.. employee.Attributes
                .OrderBy(attribute => attribute.Key, StringComparer.Ordinal)
                .Select(attribute => new EmployeeAttributeDto(
                    attribute.Key, attribute.Value, attribute.SetAt))]);
    }
}
