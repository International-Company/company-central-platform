namespace CCP.Modules.Authorization.Contracts.Dtos;

/// <summary>A role, as callers see it.</summary>
public sealed record RoleDto(
    Guid Id,
    string Code,
    string NameAr,
    string NameEn,
    string? Description,
    bool IsSystem,
    bool IsActive,
    int PermissionCount);

/// <summary>A declared permission.</summary>
public sealed record PermissionDto(
    Guid Id,
    string Name,
    string Application,
    string Resource,
    string Action,
    string Description,
    bool IsActive);

/// <summary>One role assignment.</summary>
public sealed record UserRoleDto(
    Guid Id,
    Guid UserId,
    Guid RoleId,
    string ScopeType,
    Guid? ScopeUnitId,
    Guid GrantedBy,
    DateTimeOffset GrantedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt);

/// <summary>
/// The caller's own permissions, for the frontend to hide controls with.
/// <para>
/// Names only, not scopes. The frontend uses this for usability, and the backend
/// enforces independently — sending the full scope detail would invite a client
/// to make access decisions the server is responsible for (P6).
/// </para>
/// </summary>
public sealed record MyPermissionsDto(IReadOnlyList<string> Permissions, bool HasOrganizationalUnit);

/// <summary>
/// The answer to a permission check, filter included.
/// </summary>
/// <param name="IsGranted">Whether the permission is held.</param>
/// <param name="Scope">How far it reaches.</param>
/// <param name="UnitPathPrefixes">
/// The organizational paths the caller may reach, for the consuming application
/// to apply to its own data. Empty when the scope is All or Self.
/// </param>
public sealed record PermissionCheckDto(
    bool IsGranted,
    string Scope,
    IReadOnlyList<string> UnitPathPrefixes);
