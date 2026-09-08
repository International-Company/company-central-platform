using CCP.Kernel.Results;

namespace CCP.Modules.Authorization.Domain.Permissions;

/// <summary>
/// A permission name: <c>&lt;application&gt;.&lt;resource&gt;.&lt;action&gt;</c>.
/// <para>
/// <b>This type is how the Platform stays business-agnostic while being the
/// authorization authority for business systems</b> (ADR-007 §14.2, ADR-012).
/// The Platform owns the <c>platform.*</c> namespace and nothing else. A
/// registered application owns its own — <c>finance.invoices.approve</c>,
/// <c>hr.leave-requests.view</c> — and the Platform stores and evaluates those
/// without understanding what they mean. To it, they are opaque strings attached
/// to a role.
/// </para>
/// <para>
/// That is what resolves the apparent contradiction in the brief: the Platform
/// must know nothing about business systems, and must be the single place their
/// permissions are defined. It can be both because it never interprets the
/// string.
/// </para>
/// </summary>
public sealed record PermissionName
{
    /// <summary>The namespace the Platform reserves for itself.</summary>
    public const string PlatformNamespace = "platform";

    public const int MaxLength = 128;

    private PermissionName(string application, string resource, string action)
    {
        Application = application;
        Resource = resource;
        Action = action;
        Value = $"{application}.{resource}.{action}";
    }

    /// <summary>The owning application's code — its namespace.</summary>
    public string Application { get; }

    /// <summary>What is being acted on, e.g. <c>users</c>, <c>invoices</c>.</summary>
    public string Resource { get; }

    /// <summary>What is being done, e.g. <c>view</c>, <c>create</c>, <c>approve</c>.</summary>
    public string Action { get; }

    /// <summary>The full name. This is what is stored and compared.</summary>
    public string Value { get; }

    public bool IsPlatformPermission
        => string.Equals(Application, PlatformNamespace, StringComparison.Ordinal);

    public static Result<PermissionName> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<PermissionName>(AuthorizationErrors.PermissionNameRequired);
        }

        string trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
        {
            return Result.Failure<PermissionName>(AuthorizationErrors.PermissionNameTooLong);
        }

        string[] parts = trimmed.Split('.');

        // Exactly three segments. Two would make the namespace ambiguous; four
        // would let one application's name collide with another's sub-resource.
        if (parts.Length != 3)
        {
            return Result.Failure<PermissionName>(AuthorizationErrors.PermissionNameShape);
        }

        foreach (string part in parts)
        {
            Result segmentResult = ValidateSegment(part);

            if (segmentResult.IsFailure)
            {
                return Result.Failure<PermissionName>(segmentResult.Errors);
            }
        }

        return Result.Success(new PermissionName(
            parts[0].ToLowerInvariant(),
            parts[1].ToLowerInvariant(),
            parts[2].ToLowerInvariant()));
    }

    /// <summary>
    /// Whether this name belongs to the given application's namespace.
    /// <para>
    /// The check that stops one application declaring permissions in another's
    /// namespace — which would let a registered system grant itself access to a
    /// different one's resources.
    /// </para>
    /// </summary>
    public bool BelongsTo(string applicationCode)
        => string.Equals(Application, applicationCode, StringComparison.OrdinalIgnoreCase);

    private static Result ValidateSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return Result.Failure(AuthorizationErrors.PermissionNameShape);
        }

        foreach (char c in segment)
        {
            // An allow-list. Permission names appear in tokens, audit records,
            // configuration and log lines; anything not plainly safe is refused.
            if (!char.IsAsciiLetterOrDigit(c) && c is not '-')
            {
                return Result.Failure(AuthorizationErrors.PermissionNameCharacters);
            }
        }

        return Result.Success();
    }

    public override string ToString() => Value;
}
