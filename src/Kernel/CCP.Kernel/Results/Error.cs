namespace CCP.Kernel.Results;

/// <summary>
/// A failure, described in a way that is useful to both a machine and a human.
/// <para>
/// <see cref="Code"/> is stable and machine-readable; clients may branch on it
/// and it is never localized. <see cref="Message"/> is human text and may be
/// replaced with a localized string at the API boundary.
/// </para>
/// </summary>
public sealed record Error
{
    /// <summary>Represents the absence of an error.</summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Unexpected);

    public Error(string code, string message, ErrorType type, string? field = null)
    {
        Code = code;
        Message = message;
        Type = type;
        Field = field;
    }

    /// <summary>Stable machine-readable identifier, e.g. <c>PLATFORM.USER_NOT_FOUND</c>.</summary>
    public string Code { get; }

    /// <summary>Human-readable description. Safe to show a user; never contains internals.</summary>
    public string Message { get; }

    /// <summary>The failure category, used to select an HTTP status code.</summary>
    public ErrorType Type { get; }

    /// <summary>The input field this error relates to, when it relates to one.</summary>
    public string? Field { get; }

    public static Error Validation(string code, string message, string? field = null)
        => new(code, message, ErrorType.Validation, field);

    public static Error NotFound(string code, string message)
        => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message)
        => new(code, message, ErrorType.Conflict);

    public static Error Forbidden(string code, string message)
        => new(code, message, ErrorType.Forbidden);

    public static Error Unauthenticated(string code, string message)
        => new(code, message, ErrorType.Unauthenticated);

    public static Error Rule(string code, string message)
        => new(code, message, ErrorType.Rule);

    public static Error Unexpected(string code, string message)
        => new(code, message, ErrorType.Unexpected);
}
