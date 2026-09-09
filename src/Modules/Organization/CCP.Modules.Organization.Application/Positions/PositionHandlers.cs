using CCP.Kernel.Application.Auditing;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Organization.Application.Abstractions;
using CCP.Modules.Organization.Contracts.Dtos;
using CCP.Modules.Organization.Domain;
using CCP.Modules.Organization.Domain.Positions;
using CCP.Modules.Organization.Domain.Units;

namespace CCP.Modules.Organization.Application.Positions;

/// <summary>Listing the job positions.</summary>
public sealed record GetPositionsQuery(bool IncludeInactive);

/// <summary>Creating one.</summary>
public sealed record CreatePositionCommand(
    string Code,
    string TitleAr,
    string TitleEn,
    int? Level);

/// <summary>Renaming one. The code is fixed, like every other code here.</summary>
public sealed record RenamePositionCommand(Guid PositionId, string TitleAr, string TitleEn);

/// <summary>Turning one off, or back on.</summary>
public sealed record SetPositionActiveCommand(Guid PositionId, bool IsActive);

/// <summary>
/// The positions this company has.
/// <para>
/// Empty before the company exists, for the same reason the unit tree is: a
/// Platform that has just been installed has no positions, and that is what
/// empty means.
/// </para>
/// </summary>
public sealed class GetPositionsHandler(IOrganizationRepository repository)
{
    public async Task<Result<IReadOnlyList<PositionDto>>> HandleAsync(
        GetPositionsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        Domain.Companies.Company? company = await repository.GetSingleCompanyAsync(cancellationToken);

        if (company is null)
        {
            return Result.Success<IReadOnlyList<PositionDto>>([]);
        }

        IReadOnlyList<Position> positions =
            await repository.GetPositionsAsync(company.Id, query.IncludeInactive, cancellationToken);

        return Result.Success<IReadOnlyList<PositionDto>>([.. positions.Select(ToDto)]);
    }

    internal static PositionDto ToDto(Position position)
        => new(
            position.Id,
            position.Code,
            new LocalizedNameDto(position.Title.Arabic, position.Title.English),
            position.Level,
            position.IsActive);
}

/// <summary>
/// Creating a job position.
/// <para>
/// <b>A position is not a role.</b> It describes what someone does in the
/// organization; a role describes what they may do in the software. Keeping them
/// separate is what stops a job title change from silently altering access, and
/// a permission grant from needing an HR justification. They are managed on
/// different screens, behind different permissions, on purpose.
/// </para>
/// </summary>
public sealed class CreatePositionHandler(
    IOrganizationRepository repository,
    IOrganizationUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IClock clock)
{
    private const string ModuleName = "organization";

    public async Task<Result<PositionDto>> HandleAsync(
        CreatePositionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Domain.Companies.Company? company = await repository.GetSingleCompanyAsync(cancellationToken);

        if (company is null)
        {
            // A write, so it refuses. A position belongs to a company, and one
            // created without it would be an orphan nothing could find.
            return Result.Failure<PositionDto>(OrganizationErrors.CompanyNotFound);
        }

        string code = command.Code.Trim().ToUpperInvariant();

        if (await repository.PositionCodeExistsAsync(company.Id, code, cancellationToken))
        {
            return Result.Failure<PositionDto>(OrganizationErrors.PositionCodeTaken);
        }

        Result<LocalizedName> title = LocalizedName.Create(command.TitleAr, command.TitleEn);

        if (title.IsFailure)
        {
            return Result.Failure<PositionDto>(title.Errors);
        }

        Result<Position> position = Position.Create(
            company.Id, command.Code, title.Value, command.Level, clock.UtcNow);

        if (position.IsFailure)
        {
            return Result.Failure<PositionDto>(position.Errors);
        }

        repository.AddPosition(position.Value);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                ModuleName,
                "position.created",
                AuditOutcome.Success,
                "position",
                position.Value.Id.ToString(),
                NewValue: $$"""{"code":"{{position.Value.Code}}"}"""),
            cancellationToken);

        return Result.Success(GetPositionsHandler.ToDto(position.Value));
    }
}

/// <summary>Renaming a position in both languages.</summary>
public sealed class RenamePositionHandler(
    IOrganizationRepository repository,
    IOrganizationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<PositionDto>> HandleAsync(
        RenamePositionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Position? position = await repository.FindPositionAsync(command.PositionId, cancellationToken);

        if (position is null)
        {
            return Result.Failure<PositionDto>(OrganizationErrors.PositionNotFound);
        }

        Result<LocalizedName> title = LocalizedName.Create(command.TitleAr, command.TitleEn);

        if (title.IsFailure)
        {
            return Result.Failure<PositionDto>(title.Errors);
        }

        Result renamed = position.Rename(title.Value, clock.UtcNow);

        if (renamed.IsFailure)
        {
            return Result.Failure<PositionDto>(renamed.Errors);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(GetPositionsHandler.ToDto(position));
    }
}

/// <summary>
/// Turning a position off, or back on.
/// <para>
/// Deactivated, never deleted: employees who held it still refer to it, and a
/// position that vanishes takes the explanation of somebody's past role with it.
/// </para>
/// </summary>
public sealed class SetPositionActiveHandler(
    IOrganizationRepository repository,
    IOrganizationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        SetPositionActiveCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Position? position = await repository.FindPositionAsync(command.PositionId, cancellationToken);

        if (position is null)
        {
            return Result.Failure(OrganizationErrors.PositionNotFound);
        }

        DateTimeOffset now = clock.UtcNow;

        Result changed = command.IsActive ? position.Reactivate(now) : position.Deactivate(now);

        if (changed.IsFailure)
        {
            return changed;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
