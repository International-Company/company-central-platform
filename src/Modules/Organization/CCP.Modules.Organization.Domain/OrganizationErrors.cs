using CCP.Kernel.Results;

namespace CCP.Modules.Organization.Domain;

/// <summary>
/// Every failure the Organization module can produce.
/// <para>
/// Codes are part of the API contract (ADR-008): stable, never localized, safe
/// for a client to branch on. Unlike Identity's, none of these need to be
/// uniform — organizational structure is not secret inside the company, so a
/// precise message here helps an administrator rather than an attacker.
/// </para>
/// </summary>
public static class OrganizationErrors
{
    // --- Names -------------------------------------------------------------

    public static readonly Error NameArabicRequired = Error.Validation(
        "ORGANIZATION.NAME_AR_REQUIRED", "An Arabic name is required.", "nameAr");

    public static readonly Error NameEnglishRequired = Error.Validation(
        "ORGANIZATION.NAME_EN_REQUIRED", "An English name is required.", "nameEn");

    public static readonly Error NameArabicTooLong = Error.Validation(
        "ORGANIZATION.NAME_AR_TOO_LONG", "The Arabic name is too long.", "nameAr");

    public static readonly Error NameEnglishTooLong = Error.Validation(
        "ORGANIZATION.NAME_EN_TOO_LONG", "The English name is too long.", "nameEn");

    // --- Codes -------------------------------------------------------------

    public static readonly Error CodeRequired = Error.Validation(
        "ORGANIZATION.CODE_REQUIRED", "A code is required.", "code");

    public static readonly Error CodeLength = Error.Validation(
        "ORGANIZATION.CODE_LENGTH", "The code must be between 2 and 32 characters.", "code");

    public static readonly Error CodeCharacters = Error.Validation(
        "ORGANIZATION.CODE_CHARACTERS",
        "The code may contain only letters, digits, hyphen, underscore and dot.",
        "code");

    public static readonly Error CodeTaken = Error.Conflict(
        "ORGANIZATION.CODE_TAKEN", "That code is already in use.");

    // --- Units -------------------------------------------------------------

    public static readonly Error UnitNotFound = Error.NotFound(
        "ORGANIZATION.UNIT_NOT_FOUND", "The organizational unit does not exist.");

    public static readonly Error ParentNotFound = Error.NotFound(
        "ORGANIZATION.PARENT_NOT_FOUND", "The parent unit does not exist.");

    public static readonly Error ParentInDifferentCompany = Error.Rule(
        "ORGANIZATION.PARENT_IN_DIFFERENT_COMPANY",
        "A unit cannot sit under a parent belonging to a different company.");

    public static readonly Error UnitCannotBeItsOwnParent = Error.Rule(
        "ORGANIZATION.UNIT_CANNOT_BE_OWN_PARENT", "A unit cannot be its own parent.");

    /// <summary>
    /// The cycle that matters. Moving a unit beneath its own descendant would
    /// detach that branch from the root entirely, and every scope check over it
    /// would then be wrong.
    /// </summary>
    public static readonly Error UnitCannotMoveUnderOwnDescendant = Error.Rule(
        "ORGANIZATION.UNIT_CANNOT_MOVE_UNDER_DESCENDANT",
        "A unit cannot be moved beneath one of its own descendants.");

    public static readonly Error UnitAlreadyActive = Error.Conflict(
        "ORGANIZATION.UNIT_ALREADY_ACTIVE", "The unit is already active.");

    public static readonly Error UnitAlreadyInactive = Error.Conflict(
        "ORGANIZATION.UNIT_ALREADY_INACTIVE", "The unit is already inactive.");

    public static readonly Error UnitHasActiveChildren = Error.Conflict(
        "ORGANIZATION.UNIT_HAS_ACTIVE_CHILDREN",
        "The unit still has active child units. Deactivate or move them first.");

    public static readonly Error UnitHasActiveEmployees = Error.Conflict(
        "ORGANIZATION.UNIT_HAS_ACTIVE_EMPLOYEES",
        "The unit still has active employees. Transfer them first.");

    // --- Positions ---------------------------------------------------------

    public static readonly Error PositionCodeTaken = Error.Conflict(
        "ORGANIZATION.POSITION_CODE_TAKEN",
        "A position with this code already exists in this company.");

    public static readonly Error PositionNotFound = Error.NotFound(
        "ORGANIZATION.POSITION_NOT_FOUND", "The position does not exist.");

    public static readonly Error PositionInactive = Error.Rule(
        "ORGANIZATION.POSITION_INACTIVE", "The position is not active.");

    // --- Employees ---------------------------------------------------------

    public static readonly Error EmployeeNotFound = Error.NotFound(
        "ORGANIZATION.EMPLOYEE_NOT_FOUND", "The employee does not exist.");

    public static readonly Error EmployeeNumberTaken = Error.Conflict(
        "ORGANIZATION.EMPLOYEE_NUMBER_TAKEN", "That employee number is already in use.");

    public static readonly Error EmployeeNumberRequired = Error.Validation(
        "ORGANIZATION.EMPLOYEE_NUMBER_REQUIRED", "An employee number is required.", "employeeNumber");

    public static readonly Error UserAlreadyLinked = Error.Conflict(
        "ORGANIZATION.USER_ALREADY_LINKED",
        "That user account is already linked to another employee.");

    public static readonly Error EmployeeCannotManageThemselves = Error.Rule(
        "ORGANIZATION.EMPLOYEE_CANNOT_MANAGE_SELF", "An employee cannot be their own manager.");

    /// <summary>
    /// A reporting cycle. Left unchecked, walking the management chain — which
    /// the workflow engine does to find an approver — would loop forever.
    /// </summary>
    public static readonly Error ManagementCycle = Error.Rule(
        "ORGANIZATION.MANAGEMENT_CYCLE",
        "That manager reports to this employee, directly or indirectly. "
        + "Setting it would create a reporting loop.");

    public static readonly Error EmployeeAlreadyActive = Error.Conflict(
        "ORGANIZATION.EMPLOYEE_ALREADY_ACTIVE", "The employee is already active.");

    public static readonly Error EmployeeAlreadyInactive = Error.Conflict(
        "ORGANIZATION.EMPLOYEE_ALREADY_INACTIVE", "The employee is already inactive.");

    public static readonly Error UnitInactive = Error.Rule(
        "ORGANIZATION.UNIT_INACTIVE", "The unit is not active.");

    // --- Company -----------------------------------------------------------

    public static readonly Error CompanyNotFound = Error.NotFound(
        "ORGANIZATION.COMPANY_NOT_FOUND", "The company does not exist.");

    public static readonly Error CompanyAlreadyExists = Error.Conflict(
        "ORGANIZATION.COMPANY_ALREADY_EXISTS",
        "A company already exists. The Platform is configured for a single company.");
}
