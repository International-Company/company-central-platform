using CCP.Kernel.Results;

namespace CCP.Modules.Authorization.Domain;

/// <summary>
/// Every failure the Authorization module can produce.
/// <para>
/// Codes are part of the API contract (ADR-008): stable, never localized, safe
/// to branch on. Denials are precise rather than uniform — unlike Identity's
/// sign-in, telling an authenticated caller which permission they lack helps
/// them ask for the right thing, and reveals nothing they could not discover by
/// trying.
/// </para>
/// </summary>
public static class AuthorizationErrors
{
    // --- Permission names --------------------------------------------------

    public static readonly Error PermissionNameRequired = Error.Validation(
        "AUTHZ.PERMISSION_NAME_REQUIRED", "A permission name is required.", "permission");

    public static readonly Error PermissionNameShape = Error.Validation(
        "AUTHZ.PERMISSION_NAME_SHAPE",
        "A permission name must have exactly three parts: application.resource.action.",
        "permission");

    public static readonly Error PermissionNameCharacters = Error.Validation(
        "AUTHZ.PERMISSION_NAME_CHARACTERS",
        "Permission name parts may contain only letters, digits and hyphen.",
        "permission");

    public static readonly Error PermissionNameTooLong = Error.Validation(
        "AUTHZ.PERMISSION_NAME_TOO_LONG", "The permission name is too long.", "permission");

    /// <summary>
    /// The check that stops a registered system declaring permissions in another
    /// application's namespace — which would let it grant itself the Platform's
    /// own administrative rights.
    /// </summary>
    public static Error PermissionOutsideApplicationNamespace(string applicationCode) => Error.Rule(
        "AUTHZ.PERMISSION_OUTSIDE_NAMESPACE",
        $"A permission declared by '{applicationCode}' must begin with '{applicationCode}.'.");

    public static readonly Error PermissionNotFound = Error.NotFound(
        "AUTHZ.PERMISSION_NOT_FOUND", "The permission does not exist.");

    // --- Applications ------------------------------------------------------

    public static readonly Error ApplicationCodeRequired = Error.Validation(
        "AUTHZ.APPLICATION_CODE_REQUIRED", "An application code is required.", "code");

    public static readonly Error ApplicationCodeLength = Error.Validation(
        "AUTHZ.APPLICATION_CODE_LENGTH", "The application code must be 2 to 32 characters.", "code");

    public static readonly Error ApplicationCodeCharacters = Error.Validation(
        "AUTHZ.APPLICATION_CODE_CHARACTERS",
        "The application code may contain only letters, digits and hyphen.",
        "code");

    public static readonly Error ApplicationNameRequired = Error.Validation(
        "AUTHZ.APPLICATION_NAME_REQUIRED", "An application name is required.", "name");

    public static readonly Error ReservedApplicationCode = Error.Rule(
        "AUTHZ.RESERVED_APPLICATION_CODE",
        "The 'platform' namespace is reserved for the Platform itself.");

    public static readonly Error ApplicationNotFound = Error.NotFound(
        "AUTHZ.APPLICATION_NOT_FOUND", "The application is not registered.");

    public static readonly Error ApplicationCodeTaken = Error.Conflict(
        "AUTHZ.APPLICATION_CODE_TAKEN", "That application code is already registered.");

    public static readonly Error ApplicationAlreadyActive = Error.Conflict(
        "AUTHZ.APPLICATION_ALREADY_ACTIVE", "The application is already active.");

    public static readonly Error ApplicationAlreadyInactive = Error.Conflict(
        "AUTHZ.APPLICATION_ALREADY_INACTIVE", "The application is already inactive.");

    public static readonly Error CannotModifySystemApplication = Error.Rule(
        "AUTHZ.CANNOT_MODIFY_SYSTEM_APPLICATION",
        "The Platform's own registration cannot be modified.");

    // --- Roles -------------------------------------------------------------

    public static readonly Error RoleCodeRequired = Error.Validation(
        "AUTHZ.ROLE_CODE_REQUIRED", "A role code is required.", "code");

    public static readonly Error RoleCodeLength = Error.Validation(
        "AUTHZ.ROLE_CODE_LENGTH", "The role code must be 2 to 64 characters.", "code");

    public static readonly Error RoleCodeCharacters = Error.Validation(
        "AUTHZ.ROLE_CODE_CHARACTERS",
        "The role code may contain only letters, digits, hyphen and underscore.",
        "code");

    public static readonly Error RoleNameArabicRequired = Error.Validation(
        "AUTHZ.ROLE_NAME_AR_REQUIRED", "An Arabic role name is required.", "nameAr");

    public static readonly Error RoleNameEnglishRequired = Error.Validation(
        "AUTHZ.ROLE_NAME_EN_REQUIRED", "An English role name is required.", "nameEn");

    public static readonly Error RoleNotFound = Error.NotFound(
        "AUTHZ.ROLE_NOT_FOUND", "The role does not exist.");

    public static readonly Error RoleCodeTaken = Error.Conflict(
        "AUTHZ.ROLE_CODE_TAKEN", "That role code is already in use.");

    public static readonly Error RoleAlreadyActive = Error.Conflict(
        "AUTHZ.ROLE_ALREADY_ACTIVE", "The role is already active.");

    public static readonly Error RoleAlreadyInactive = Error.Conflict(
        "AUTHZ.ROLE_ALREADY_INACTIVE", "The role is already inactive.");

    public static readonly Error CannotModifySystemRole = Error.Rule(
        "AUTHZ.CANNOT_MODIFY_SYSTEM_ROLE",
        "A Platform-owned role cannot be deactivated or renamed.");

    public static readonly Error RoleInactive = Error.Rule(
        "AUTHZ.ROLE_INACTIVE", "The role is not active and cannot be granted.");

    // --- Grants ------------------------------------------------------------

    /// <summary>
    /// Nobody grants themselves a role. Without this, anyone who reaches the
    /// grant endpoint at all can escalate to anything.
    /// </summary>
    public static readonly Error CannotGrantToSelf = Error.Forbidden(
        "AUTHZ.CANNOT_GRANT_TO_SELF", "You cannot grant or revoke your own roles.");

    /// <summary>
    /// The anti-escalation rule. A granter may only hand on access they hold
    /// themselves — otherwise the ability to grant is the ability to become
    /// anything.
    /// </summary>
    public static Error CannotGrantUnheldPermission(string permissionName) => Error.Forbidden(
        "AUTHZ.CANNOT_GRANT_UNHELD_PERMISSION",
        $"You cannot grant '{permissionName}' because you do not hold it yourself.");

    public static Error CannotGrantWiderScope(string scope) => Error.Forbidden(
        "AUTHZ.CANNOT_GRANT_WIDER_SCOPE",
        $"You cannot grant a scope of '{scope}', which is wider than your own.");

    public static readonly Error ExpiryInThePast = Error.Validation(
        "AUTHZ.EXPIRY_IN_THE_PAST", "The expiry must be in the future.", "expiresAt");

    public static readonly Error ScopeUnitNotApplicable = Error.Validation(
        "AUTHZ.SCOPE_UNIT_NOT_APPLICABLE",
        "A unit may only be given for a Unit or UnitAndBelow scope.",
        "scopeUnitId");

    public static readonly Error ScopeUnitRequired = Error.Validation(
        "AUTHZ.SCOPE_UNIT_REQUIRED",
        "This scope needs a unit, and the user has no employee record to take one from.",
        "scopeUnitId");

    public static readonly Error AssignmentNotFound = Error.NotFound(
        "AUTHZ.ASSIGNMENT_NOT_FOUND", "The role assignment does not exist.");

    public static readonly Error AlreadyGranted = Error.Conflict(
        "AUTHZ.ALREADY_GRANTED", "The user already holds this role at this scope.");

    // --- Application credentials -------------------------------------------

    public static readonly Error CredentialLabelRequired = Error.Validation(
        "AUTHZ.CREDENTIAL_LABEL_REQUIRED",
        "A credential needs a label, so that revoking the right one is possible later.",
        "label");

    public static readonly Error CredentialNotFound = Error.NotFound(
        "AUTHZ.CREDENTIAL_NOT_FOUND", "The credential does not exist.");

    public static readonly Error CredentialAlreadyRevoked = Error.Conflict(
        "AUTHZ.CREDENTIAL_ALREADY_REVOKED", "The credential is already revoked.");

    public static Error TooManyLiveCredentials(int limit) => Error.Conflict(
        "AUTHZ.TOO_MANY_LIVE_CREDENTIALS",
        $"An application may hold {limit} live credentials at once. "
        + "Revoke the one being replaced before issuing another.");

    /// <summary>
    /// The token endpoint refused. One error for every reason, on purpose.
    /// </summary>
    public static readonly Error InvalidClient = Error.Unauthenticated(
        "AUTHZ.INVALID_CLIENT", "The client credentials are not valid.");

    public static readonly Error SelfScopeMeaninglessForApplication = Error.Validation(
        "AUTHZ.SELF_SCOPE_FOR_APPLICATION",
        "An application has no place in the organization, so a Self scope has nothing to follow. "
        + "Anchor the grant to a unit, or grant it company-wide.",
        "scope");

    public static readonly Error AssignmentAlreadyRevoked = Error.Conflict(
        "AUTHZ.ASSIGNMENT_ALREADY_REVOKED", "The grant is already revoked.");

    public static readonly Error ApplicationAlreadyHoldsRole = Error.Conflict(
        "AUTHZ.APPLICATION_ALREADY_GRANTED",
        "The application already holds this role at this scope.");

    public static readonly Error ApplicationInactive = Error.Forbidden(
        "AUTHZ.APPLICATION_INACTIVE",
        "The application is disabled.");

    /// <summary>
    /// An application asked to act as somebody without being allowed to.
    /// </summary>
    public static readonly Error DelegationNotPermitted = Error.Forbidden(
        "AUTHZ.DELEGATION_NOT_PERMITTED",
        "This application may not act on behalf of a user.");

    public static readonly Error DelegationSubjectNotFound = Error.Validation(
        "AUTHZ.DELEGATION_SUBJECT_NOT_FOUND",
        "The user this application asked to act for does not exist, or cannot sign in.",
        "on_behalf_of");

    // --- Access ------------------------------------------------------------

    /// <summary>
    /// The denial returned to an authenticated caller. Names the permission,
    /// because that helps them ask for the right thing and reveals nothing they
    /// could not learn by trying.
    /// </summary>
    public static Error PermissionDenied(string permissionName) => Error.Forbidden(
        "AUTHZ.PERMISSION_DENIED",
        $"This action requires the '{permissionName}' permission.");
}
