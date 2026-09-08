using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;

namespace CCP.Modules.Organization.Domain.Companies;

/// <summary>
/// The company the Platform serves.
/// <para>
/// The Platform is configured for <b>one</b> company (ARCHITECTURE.md §27 Q2 —
/// still unconfirmed by the owner). Modelling it as an entity rather than a
/// constant is the cheap hedge: if the answer turns out to be "several legal
/// entities", supporting them means relaxing a uniqueness rule, not reshaping
/// every table that already carries <c>CompanyId</c>.
/// </para>
/// <para>
/// It holds identity, not business configuration. Fiscal year, tax registration,
/// bank details and the like belong to the Financial application — putting them
/// here would be the first step in turning the Platform into an ERP (ADR-005).
/// </para>
/// </summary>
public sealed class Company : AggregateRoot, IAuditableEntity
{
    private Company() { }

    private Company(Guid id, string code, LocalizedName name, DateTimeOffset now)
        : base(id)
    {
        Code = code;
        Name = name;
        IsActive = true;
        CreatedAt = now;
    }

    public string Code { get; private set; } = string.Empty;

    public LocalizedName Name { get; private set; } = null!;

    /// <summary>Default locale for the company. Individual users may differ.</summary>
    public string DefaultLocale { get; private set; } = "ar";

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static Result<Company> Create(
        string code,
        LocalizedName name,
        string defaultLocale,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure<Company>(OrganizationErrors.CodeRequired);
        }

        return Result.Success(new Company(Uuid7.NewGuid(now), code.Trim().ToUpperInvariant(), name, now)
        {
            DefaultLocale = string.IsNullOrWhiteSpace(defaultLocale) ? "ar" : defaultLocale.Trim()
        });
    }

    public Result Rename(LocalizedName name, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(name);

        Name = name;
        UpdatedAt = now;

        return Result.Success();
    }
}
