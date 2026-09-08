namespace CCP.Modules.Organization.Contracts.Dtos;

/// <summary>
/// A name in both company languages, as callers see it.
/// <para>
/// Both are returned rather than one resolved server-side. The client knows
/// which locale it is rendering, and returning both means a language toggle
/// needs no round trip (ADR-011).
/// </para>
/// </summary>
public sealed record LocalizedNameDto(string Ar, string En);

/// <summary>One organizational unit, flat.</summary>
public sealed record OrganizationUnitDto(
    Guid Id,
    Guid? ParentId,
    string UnitType,
    string Code,
    LocalizedNameDto Name,
    int Depth,
    int SortOrder,
    bool IsActive);

/// <summary>One organizational unit with its children nested beneath it.</summary>
public sealed record OrganizationUnitTreeDto(
    Guid Id,
    Guid? ParentId,
    string UnitType,
    string Code,
    LocalizedNameDto Name,
    int Depth,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<OrganizationUnitTreeDto> Children);

/// <summary>A job position.</summary>
public sealed record PositionDto(
    Guid Id,
    string Code,
    LocalizedNameDto Title,
    int? Level,
    bool IsActive);

/// <summary>
/// An employee, as an organizational record.
/// <para>
/// Note what is absent: no salary, no leave balance, no contract, no appraisal.
/// Those are HR business data and live in the HR application, which references
/// this record by id (ADR-005 §4.3a). This DTO is the visible edge of that
/// boundary, and a field added here is a boundary decision, not a convenience.
/// </para>
/// </summary>
public sealed record EmployeeDto(
    Guid Id,
    string EmployeeNumber,
    LocalizedNameDto FullName,
    Guid? UserId,
    Guid UnitId,
    string UnitCode,
    Guid? PositionId,
    string? PositionCode,
    Guid? ManagerId,
    string? WorkEmail,
    string? WorkPhone,
    DateOnly? HireDate,
    bool IsActive);

/// <summary>One link in a reporting line, for showing who someone reports to.</summary>
public sealed record ManagerLinkDto(Guid EmployeeId, string EmployeeNumber, LocalizedNameDto FullName);
