using CCP.Kernel.Application.Auditing;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Organization.Application.Abstractions;
using CCP.Modules.Organization.Contracts.Dtos;
using CCP.Modules.Organization.Domain;
using CCP.Modules.Organization.Domain.Companies;
using CCP.Modules.Organization.Domain.Units;

namespace CCP.Modules.Organization.Application.Companies;

/// <summary>Reading the company, if there is one yet.</summary>
public sealed record GetCompanyQuery;

/// <summary>Establishing the company the Platform serves.</summary>
public sealed record CreateCompanyCommand(
    string Code,
    string NameAr,
    string NameEn,
    string DefaultLocale);

/// <summary>Renaming it.</summary>
public sealed record RenameCompanyCommand(string NameAr, string NameEn);

/// <summary>
/// The company, or nothing.
/// <para>
/// <b>Absence is an answer, not a failure.</b> A Platform that has just been
/// installed has no company, and a caller asking "which company is this?"
/// deserves "none yet" rather than a 404 they have to interpret. The screen that
/// asks is the one that offers to create it.
/// </para>
/// </summary>
public sealed class GetCompanyHandler(IOrganizationRepository repository)
{
    public async Task<Result<CompanyDto?>> HandleAsync(
        GetCompanyQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        Company? company = await repository.GetSingleCompanyAsync(cancellationToken);

        return Result.Success<CompanyDto?>(company is null ? null : ToDto(company));
    }

    internal static CompanyDto ToDto(Company company)
        => new(
            company.Id,
            company.Code,
            new LocalizedNameDto(company.Name.Arabic, company.Name.English),
            company.DefaultLocale,
            company.IsActive);
}

/// <summary>
/// Establishing the company, once.
/// <para>
/// <b>Nothing in the Organization module works until this exists.</b> A unit
/// belongs to a company, an employee belongs to a unit, and every read is scoped
/// by the company — so a Platform without one has an organization module that
/// can do nothing, and no way through the product to change that. That was the
/// state it shipped in: the module assumed a company and nothing created one.
/// </para>
/// <para>
/// Exactly one, refused after the first. The Platform serves a single company
/// (ARCHITECTURE.md §27 Q2); a second would silently divide every list in two,
/// since every query resolves "the" company and would then find the wrong one.
/// </para>
/// </summary>
public sealed class CreateCompanyHandler(
    IOrganizationRepository repository,
    IOrganizationUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IClock clock)
{
    private const string ModuleName = "organization";

    public async Task<Result<CompanyDto>> HandleAsync(
        CreateCompanyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await repository.AnyCompanyExistsAsync(cancellationToken))
        {
            return Result.Failure<CompanyDto>(OrganizationErrors.CompanyAlreadyExists);
        }

        Result<LocalizedName> name = LocalizedName.Create(command.NameAr, command.NameEn);

        if (name.IsFailure)
        {
            return Result.Failure<CompanyDto>(name.Errors);
        }

        Result<Company> company = Company.Create(
            command.Code, name.Value, command.DefaultLocale, clock.UtcNow);

        if (company.IsFailure)
        {
            return Result.Failure<CompanyDto>(company.Errors);
        }

        repository.AddCompany(company.Value);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                ModuleName,
                "company.created",
                AuditOutcome.Success,
                "company",
                company.Value.Id.ToString(),
                NewValue: $$"""{"code":"{{company.Value.Code}}"}"""),
            cancellationToken);

        return Result.Success(GetCompanyHandler.ToDto(company.Value));
    }
}

/// <summary>Renaming the company. Its code is fixed, like every other code here.</summary>
public sealed class RenameCompanyHandler(
    IOrganizationRepository repository,
    IOrganizationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<CompanyDto>> HandleAsync(
        RenameCompanyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Company? company = await repository.GetSingleCompanyAsync(cancellationToken);

        if (company is null)
        {
            return Result.Failure<CompanyDto>(OrganizationErrors.CompanyNotFound);
        }

        Result<LocalizedName> name = LocalizedName.Create(command.NameAr, command.NameEn);

        if (name.IsFailure)
        {
            return Result.Failure<CompanyDto>(name.Errors);
        }

        Result renamed = company.Rename(name.Value, clock.UtcNow);

        if (renamed.IsFailure)
        {
            return Result.Failure<CompanyDto>(renamed.Errors);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(GetCompanyHandler.ToDto(company));
    }
}
