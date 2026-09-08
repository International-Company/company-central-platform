using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Organization.Application.Abstractions;
using CCP.Modules.Organization.Contracts.Dtos;
using CCP.Modules.Organization.Domain;
using CCP.Modules.Organization.Domain.Units;

namespace CCP.Modules.Organization.Application.Units;

/// <summary>Creating a unit.</summary>
public sealed record CreateUnitCommand(
    Guid? ParentId,
    OrganizationUnitType UnitType,
    string Code,
    string NameAr,
    string NameEn);

public sealed class CreateUnitHandler(
    IOrganizationRepository repository,
    IOrganizationOutbox outbox,
    IOrganizationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<OrganizationUnitDto>> HandleAsync(
        CreateUnitCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        Domain.Companies.Company? company = await repository.GetSingleCompanyAsync(cancellationToken);

        if (company is null)
        {
            return Result.Failure<OrganizationUnitDto>(OrganizationErrors.CompanyNotFound);
        }

        OrganizationUnit? parent = null;

        if (command.ParentId is { } parentId)
        {
            parent = await repository.FindUnitAsync(parentId, cancellationToken);

            if (parent is null)
            {
                return Result.Failure<OrganizationUnitDto>(OrganizationErrors.ParentNotFound);
            }
        }

        Result<LocalizedName> name = LocalizedName.Create(command.NameAr, command.NameEn);

        if (name.IsFailure)
        {
            return Result.Failure<OrganizationUnitDto>(name.Errors);
        }

        if (await repository.UnitCodeExistsAsync(company.Id, command.Code, null, cancellationToken))
        {
            return Result.Failure<OrganizationUnitDto>(OrganizationErrors.CodeTaken);
        }

        Result<OrganizationUnit> creation = OrganizationUnit.Create(
            company.Id, parent, command.UnitType, command.Code, name.Value, now);

        if (creation.IsFailure)
        {
            return Result.Failure<OrganizationUnitDto>(creation.Errors);
        }

        repository.AddUnit(creation.Value);

        await PublishDomainEventsAsync(creation.Value, outbox, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(OrganizationMapper.ToDto(creation.Value));
    }

    internal static async Task PublishDomainEventsAsync(
        Kernel.Domain.AggregateRoot aggregate,
        IOrganizationOutbox outbox,
        CancellationToken cancellationToken)
    {
        foreach (Kernel.Domain.IDomainEvent domainEvent in aggregate.DomainEvents)
        {
            if (domainEvent is Kernel.Domain.IIntegrationEvent integrationEvent)
            {
                await outbox.EnqueueAsync(integrationEvent, cancellationToken);
            }
        }

        aggregate.ClearDomainEvents();
    }
}

/// <summary>Moving a unit to a new parent.</summary>
public sealed record MoveUnitCommand(Guid UnitId, Guid? NewParentId);

/// <summary>
/// Moves a unit, rewriting the materialized path of every descendant.
/// <para>
/// <b>This is the highest-risk operation in the module.</b> The move itself is a
/// single row, but the path rewrite touches the entire subtree, and every one of
/// those rows must land in the same transaction. A partial rewrite would leave
/// units whose path claims an ancestry they no longer have — and from Phase 4
/// that means authorization scope resolving against a tree that does not exist,
/// silently granting or denying the wrong access.
/// </para>
/// <para>
/// The descendants are loaded before the move, because afterwards the moved
/// unit's path no longer matches theirs and the prefix query would find nothing.
/// </para>
/// </summary>
public sealed class MoveUnitHandler(
    IOrganizationRepository repository,
    IOrganizationOutbox outbox,
    IOrganizationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        MoveUnitCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        OrganizationUnit? unit = await repository.FindUnitAsync(command.UnitId, cancellationToken);

        if (unit is null)
        {
            return Result.Failure(OrganizationErrors.UnitNotFound);
        }

        OrganizationUnit? newParent = null;

        if (command.NewParentId is { } parentId)
        {
            newParent = await repository.FindUnitAsync(parentId, cancellationToken);

            if (newParent is null)
            {
                return Result.Failure(OrganizationErrors.ParentNotFound);
            }
        }

        // Loaded first. After the move the unit's path has changed, and a prefix
        // query against the new path would return nothing.
        IReadOnlyList<OrganizationUnit> descendants =
            await repository.GetDescendantsAsync(unit, cancellationToken);

        Result<PathChange> move = unit.MoveTo(newParent, now);

        if (move.IsFailure)
        {
            return Result.Failure(move.Errors);
        }

        if (move.Value.IsNoOp)
        {
            return Result.Success();
        }

        foreach (OrganizationUnit descendant in descendants)
        {
            descendant.RebaseUnder(move.Value, now);
        }

        await CreateUnitHandler.PublishDomainEventsAsync(unit, outbox, cancellationToken);

        // One SaveChanges, one transaction: the moved unit and every rebased
        // descendant commit together or not at all.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Renaming a unit.</summary>
public sealed record RenameUnitCommand(Guid UnitId, string NameAr, string NameEn);

public sealed class RenameUnitHandler(
    IOrganizationRepository repository,
    IOrganizationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<OrganizationUnitDto>> HandleAsync(
        RenameUnitCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        OrganizationUnit? unit = await repository.FindUnitAsync(command.UnitId, cancellationToken);

        if (unit is null)
        {
            return Result.Failure<OrganizationUnitDto>(OrganizationErrors.UnitNotFound);
        }

        Result<LocalizedName> name = LocalizedName.Create(command.NameAr, command.NameEn);

        if (name.IsFailure)
        {
            return Result.Failure<OrganizationUnitDto>(name.Errors);
        }

        unit.Rename(name.Value, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(OrganizationMapper.ToDto(unit));
    }
}

/// <summary>Deactivating a unit.</summary>
public sealed record DeactivateUnitCommand(Guid UnitId);

/// <summary>
/// Deactivates a unit, refusing while anything still depends on it.
/// <para>
/// A unit with active children or employees cannot be deactivated. Allowing it
/// would leave people and sub-units attached to something the organization no
/// longer considers real — and their organizational scope would resolve against
/// a dead branch.
/// </para>
/// </summary>
public sealed class DeactivateUnitHandler(
    IOrganizationRepository repository,
    IOrganizationOutbox outbox,
    IOrganizationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        DeactivateUnitCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        OrganizationUnit? unit = await repository.FindUnitAsync(command.UnitId, cancellationToken);

        if (unit is null)
        {
            return Result.Failure(OrganizationErrors.UnitNotFound);
        }

        if (await repository.HasActiveChildrenAsync(unit.Id, cancellationToken))
        {
            return Result.Failure(OrganizationErrors.UnitHasActiveChildren);
        }

        if (await repository.HasActiveEmployeesAsync(unit.Id, cancellationToken))
        {
            return Result.Failure(OrganizationErrors.UnitHasActiveEmployees);
        }

        Result result = unit.Deactivate(clock.UtcNow);

        if (result.IsFailure)
        {
            return result;
        }

        await CreateUnitHandler.PublishDomainEventsAsync(unit, outbox, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>The whole tree for one company.</summary>
public sealed record GetUnitTreeQuery(bool IncludeInactive);

/// <summary>
/// Returns the company structure as a tree.
/// <para>
/// Loads every unit in one query and assembles the tree in memory. For an
/// organization of a few thousand units that is a single indexed read and some
/// dictionary work — far cheaper than a recursive query per level, and it lets
/// the caller render the whole chart without a round trip per node.
/// </para>
/// </summary>
public sealed class GetUnitTreeHandler(IOrganizationRepository repository)
{
    public async Task<Result<IReadOnlyList<OrganizationUnitTreeDto>>> HandleAsync(
        GetUnitTreeQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        Domain.Companies.Company? company = await repository.GetSingleCompanyAsync(cancellationToken);

        if (company is null)
        {
            return Result.Failure<IReadOnlyList<OrganizationUnitTreeDto>>(
                OrganizationErrors.CompanyNotFound);
        }

        IReadOnlyList<OrganizationUnit> units =
            await repository.GetUnitTreeAsync(company.Id, query.IncludeInactive, cancellationToken);

        return Result.Success(OrganizationMapper.BuildTree(units));
    }
}
