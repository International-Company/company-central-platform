namespace CCP.Kernel.Results;

/// <summary>
/// The category of a failure. This is what the API layer maps to an HTTP status
/// code, so the Application layer never needs to know about HTTP.
/// </summary>
public enum ErrorType
{
    /// <summary>Input failed validation. Maps to 422.</summary>
    Validation = 1,

    /// <summary>The requested resource does not exist. Maps to 404.</summary>
    NotFound = 2,

    /// <summary>The request conflicts with current state. Maps to 409.</summary>
    Conflict = 3,

    /// <summary>The caller is not authenticated. Maps to 401.</summary>
    Unauthenticated = 4,

    /// <summary>The caller is authenticated but not permitted. Maps to 403.</summary>
    Forbidden = 5,

    /// <summary>A business rule was violated. Maps to 400.</summary>
    Rule = 6,

    /// <summary>An unexpected failure. Maps to 500.</summary>
    Unexpected = 7
}
