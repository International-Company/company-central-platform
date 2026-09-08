using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;

namespace CCP.Modules.Authorization.Domain.Applications;

/// <summary>
/// A system registered to use the Platform (ADR-012).
/// <para>
/// <b>This is the mechanism by which the Platform serves systems that do not
/// exist yet.</b> A future Financial or HR system registers here, declares its
/// permissions under its own namespace, and the Platform stores and evaluates
/// them without a single line of Platform code changing. Applications,
/// permissions and roles are <i>data</i>; data can be added without a
/// deployment, code cannot.
/// </para>
/// <para>
/// The Platform itself is registered as an application with the reserved code
/// <c>platform</c>, so its own permissions go through exactly the same
/// machinery. A mechanism that the Platform exempts itself from is a mechanism
/// nobody trusts.
/// </para>
/// </summary>
public sealed class RegisteredApplication : AggregateRoot, IAuditableEntity
{
    private RegisteredApplication() { }

    private RegisteredApplication(Guid id, string code, string name, DateTimeOffset now)
        : base(id)
    {
        Code = code;
        Name = name;
        IsActive = true;
        CreatedAt = now;
    }

    /// <summary>
    /// The application's namespace. Every permission it declares must begin with
    /// this, which is what stops one system granting itself access to another's
    /// resources.
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>
    /// True for the Platform's own registration. A system application cannot be
    /// deleted or renamed — the Platform's permissions depend on its namespace
    /// remaining exactly what every endpoint declares.
    /// </summary>
    public bool IsSystem { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static Result<RegisteredApplication> Create(
        string code,
        string name,
        string? description,
        DateTimeOffset now)
    {
        Result codeResult = ValidateCode(code);

        if (codeResult.IsFailure)
        {
            return Result.Failure<RegisteredApplication>(codeResult.Errors);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<RegisteredApplication>(AuthorizationErrors.ApplicationNameRequired);
        }

        // The platform namespace is reserved. Letting a business system claim it
        // would let that system declare permissions the Platform's own endpoints
        // check for.
        if (string.Equals(code.Trim(), Permissions.PermissionName.PlatformNamespace,
                StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<RegisteredApplication>(AuthorizationErrors.ReservedApplicationCode);
        }

        return Result.Success(new RegisteredApplication(
            Uuid7.NewGuid(now), code.Trim().ToLowerInvariant(), name.Trim(), now)
        {
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim()
        });
    }

    /// <summary>
    /// Creates the Platform's own registration. Called once during seeding, and
    /// the only path that may claim the reserved namespace.
    /// </summary>
    public static RegisteredApplication CreatePlatform(DateTimeOffset now)
        => new(Uuid7.NewGuid(now), Permissions.PermissionName.PlatformNamespace,
               "Company Central Platform", now)
        {
            IsSystem = true,
            Description = "The Platform itself. Owns the platform.* permission namespace."
        };

    public Result Deactivate(DateTimeOffset now)
    {
        if (IsSystem)
        {
            return Result.Failure(AuthorizationErrors.CannotModifySystemApplication);
        }

        if (!IsActive)
        {
            return Result.Failure(AuthorizationErrors.ApplicationAlreadyInactive);
        }

        IsActive = false;
        UpdatedAt = now;

        return Result.Success();
    }

    public Result Reactivate(DateTimeOffset now)
    {
        if (IsActive)
        {
            return Result.Failure(AuthorizationErrors.ApplicationAlreadyActive);
        }

        IsActive = true;
        UpdatedAt = now;

        return Result.Success();
    }

    private static Result ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure(AuthorizationErrors.ApplicationCodeRequired);
        }

        string trimmed = code.Trim();

        if (trimmed.Length is < 2 or > 32)
        {
            return Result.Failure(AuthorizationErrors.ApplicationCodeLength);
        }

        foreach (char c in trimmed)
        {
            // Must match what a permission name segment allows, since the code
            // becomes the first segment of every permission the application
            // declares.
            if (!char.IsAsciiLetterOrDigit(c) && c is not '-')
            {
                return Result.Failure(AuthorizationErrors.ApplicationCodeCharacters);
            }
        }

        return Result.Success();
    }
}
