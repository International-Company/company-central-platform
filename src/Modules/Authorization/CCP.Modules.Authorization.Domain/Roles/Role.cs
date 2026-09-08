using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Authorization.Domain.Roles.Events;

namespace CCP.Modules.Authorization.Domain.Roles;

/// <summary>
/// A named bundle of permissions.
/// <para>
/// <b>Everything flows through roles.</b> Direct user-permission grants are
/// deliberately not supported (ADR-007 §14.1) — they are always convenient in
/// the moment, and they are the reason that, years later, nobody can answer
/// "why does this person have access to that?". With roles, a user's access is
/// the union of their roles, visible in one screen.
/// </para>
/// </summary>
public sealed class Role : AggregateRoot, IAuditableEntity
{
    private readonly List<RolePermission> _permissions = [];

    private Role() { }

    private Role(Guid id, string code, string nameAr, string nameEn, DateTimeOffset now)
        : base(id)
    {
        Code = code;
        NameAr = nameAr;
        NameEn = nameEn;
        IsActive = true;
        CreatedAt = now;
    }

    public string Code { get; private set; } = string.Empty;

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>
    /// True for roles the Platform creates and depends on. A system role cannot
    /// be deleted or have its code changed — removing the administrator role
    /// while it is the only thing granting administrative access would lock
    /// everyone out with no way back.
    /// </summary>
    public bool IsSystem { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyList<RolePermission> Permissions => _permissions.AsReadOnly();

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static Result<Role> Create(
        string code,
        string nameAr,
        string nameEn,
        string? description,
        DateTimeOffset now)
    {
        Result validation = ValidateCode(code)
            .Combine(ValidateName(nameAr, AuthorizationErrors.RoleNameArabicRequired))
            .Combine(ValidateName(nameEn, AuthorizationErrors.RoleNameEnglishRequired));

        if (validation.IsFailure)
        {
            return Result.Failure<Role>(validation.Errors);
        }

        return Result.Success(new Role(
            Uuid7.NewGuid(now), code.Trim().ToLowerInvariant(), nameAr.Trim(), nameEn.Trim(), now)
        {
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim()
        });
    }

    /// <summary>Creates a Platform-owned role. Only the seeder calls this.</summary>
    public static Role CreateSystem(
        string code,
        string nameAr,
        string nameEn,
        string description,
        DateTimeOffset now)
        => new(Uuid7.NewGuid(now), code.ToLowerInvariant(), nameAr, nameEn, now)
        {
            IsSystem = true,
            Description = description
        };

    /// <summary>
    /// Adds a permission to the role. Idempotent — granting twice is not an
    /// error, because the caller wanted a state that already holds.
    /// </summary>
    public Result AddPermission(Guid permissionId, string permissionName, DateTimeOffset now)
    {
        if (_permissions.Any(p => p.PermissionId == permissionId))
        {
            return Result.Success();
        }

        _permissions.Add(RolePermission.Create(Id, permissionId, now));
        UpdatedAt = now;

        Raise(new RolePermissionGrantedEvent(Id, Code, permissionId, permissionName, now));

        return Result.Success();
    }

    public Result RemovePermission(Guid permissionId, string permissionName, DateTimeOffset now)
    {
        RolePermission? existing = _permissions.FirstOrDefault(p => p.PermissionId == permissionId);

        if (existing is null)
        {
            return Result.Success();
        }

        _permissions.Remove(existing);
        UpdatedAt = now;

        Raise(new RolePermissionRevokedEvent(Id, Code, permissionId, permissionName, now));

        return Result.Success();
    }

    public Result Rename(string nameAr, string nameEn, string? description, DateTimeOffset now)
    {
        Result validation = ValidateName(nameAr, AuthorizationErrors.RoleNameArabicRequired)
            .Combine(ValidateName(nameEn, AuthorizationErrors.RoleNameEnglishRequired));

        if (validation.IsFailure)
        {
            return validation;
        }

        NameAr = nameAr.Trim();
        NameEn = nameEn.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        UpdatedAt = now;

        return Result.Success();
    }

    public Result Deactivate(DateTimeOffset now)
    {
        if (IsSystem)
        {
            return Result.Failure(AuthorizationErrors.CannotModifySystemRole);
        }

        if (!IsActive)
        {
            return Result.Failure(AuthorizationErrors.RoleAlreadyInactive);
        }

        IsActive = false;
        UpdatedAt = now;

        return Result.Success();
    }

    public Result Reactivate(DateTimeOffset now)
    {
        if (IsActive)
        {
            return Result.Failure(AuthorizationErrors.RoleAlreadyActive);
        }

        IsActive = true;
        UpdatedAt = now;

        return Result.Success();
    }

    private static Result ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure(AuthorizationErrors.RoleCodeRequired);
        }

        string trimmed = code.Trim();

        if (trimmed.Length is < 2 or > 64)
        {
            return Result.Failure(AuthorizationErrors.RoleCodeLength);
        }

        foreach (char c in trimmed)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))
            {
                return Result.Failure(AuthorizationErrors.RoleCodeCharacters);
            }
        }

        return Result.Success();
    }

    private static Result ValidateName(string name, Kernel.Results.Error error)
        => string.IsNullOrWhiteSpace(name) ? Result.Failure(error) : Result.Success();
}

/// <summary>Links a role to a permission it grants.</summary>
public sealed class RolePermission : Entity
{
    private RolePermission() { }

    private RolePermission(Guid id, Guid roleId, Guid permissionId, DateTimeOffset now)
        : base(id)
    {
        RoleId = roleId;
        PermissionId = permissionId;
        GrantedAt = now;
    }

    public Guid RoleId { get; private set; }

    public Guid PermissionId { get; private set; }

    public DateTimeOffset GrantedAt { get; private set; }

    internal static RolePermission Create(Guid roleId, Guid permissionId, DateTimeOffset now)
        => new(Uuid7.NewGuid(now), roleId, permissionId, now);
}
