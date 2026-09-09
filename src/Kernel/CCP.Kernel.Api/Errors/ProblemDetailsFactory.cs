using CCP.Kernel.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CCP.Kernel.Api.Errors;

/// <summary>
/// Turns a domain <see cref="Error"/> into an RFC 9457 Problem Details
/// response (ADR-008).
/// <para>
/// Every error the API returns passes through here, so the shape is identical
/// across all eleven modules and a client writes its error handling once.
/// </para>
/// </summary>
public static class ProblemDetailsFactory
{
    private const string TypeBaseUri = "https://platform.company.com/errors/";

    /// <summary>Maps a failure category to an HTTP status code.</summary>
    public static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status422UnprocessableEntity,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Unauthenticated => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.Rule => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError
    };

    /// <summary>
    /// Builds the response body for a failed result.
    /// <para>
    /// Nothing internal is ever included: no stack trace, no SQL, no
    /// connection string, no file path. The correlation id is what lets an
    /// engineer find the detail in the logs (ARCHITECTURE.md §11.3).
    /// </para>
    /// </summary>
    public static ProblemDetails Create(
        IReadOnlyList<Error> errors,
        string correlationId,
        string? instance = null)
    {
        Error primary = errors[0];
        int status = StatusCodeFor(primary.Type);

        var problem = new ProblemDetails
        {
            Type = TypeBaseUri + Slug(primary.Type),
            Title = TitleFor(primary.Type),
            Status = status,
            Detail = primary.Message,
            Instance = instance
        };

        problem.Extensions["code"] = primary.Code;
        problem.Extensions["correlationId"] = correlationId;

        // Field-level detail is only meaningful for validation failures, and
        // including it elsewhere invites leaking internal field names.
        if (primary.Type == ErrorType.Validation)
        {
            problem.Extensions["errors"] = errors
                .Select(e => new ErrorDetail(e.Field, e.Code, e.Message))
                .ToArray();
        }

        return problem;
    }

    /// <summary>
    /// The body returned when a request is refused by authorization.
    /// <para>
    /// Built here rather than from an <see cref="Error"/> because there is no
    /// domain failure to convert: the request never reached a handler. The code
    /// is what the caller acts on — it says whether to confirm a second factor
    /// or to ask for a permission.
    /// </para>
    /// </summary>
    public static ProblemDetails Forbidden(
        string code,
        string detail,
        string correlationId,
        string? instance = null)
    {
        var problem = new ProblemDetails
        {
            Type = TypeBaseUri + Slug(ErrorType.Forbidden),
            Title = TitleFor(ErrorType.Forbidden),
            Status = StatusCodes.Status403Forbidden,
            Detail = detail,
            Instance = instance
        };

        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = correlationId;

        return problem;
    }

    /// <summary>
    /// The body returned for an unhandled exception. Deliberately says nothing
    /// about what went wrong — the correlation id is the entire diagnostic
    /// surface exposed to the caller.
    /// </summary>
    public static ProblemDetails Unexpected(string correlationId, string? instance = null)
    {
        var problem = new ProblemDetails
        {
            Type = TypeBaseUri + "internal-error",
            Title = "An unexpected error occurred",
            Status = StatusCodes.Status500InternalServerError,
            Detail = "An unexpected error occurred while processing the request. "
                   + "Quote the correlation id when reporting this problem.",
            Instance = instance
        };

        problem.Extensions["code"] = "PLATFORM.INTERNAL_ERROR";
        problem.Extensions["correlationId"] = correlationId;

        return problem;
    }

    private static string TitleFor(ErrorType type) => type switch
    {
        ErrorType.Validation => "Validation failed",
        ErrorType.NotFound => "Resource not found",
        ErrorType.Conflict => "Conflict with current state",
        ErrorType.Unauthenticated => "Authentication required",
        ErrorType.Forbidden => "Permission denied",
        ErrorType.Rule => "Request cannot be processed",
        _ => "An unexpected error occurred"
    };

    private static string Slug(ErrorType type) => type switch
    {
        ErrorType.Validation => "validation-failed",
        ErrorType.NotFound => "not-found",
        ErrorType.Conflict => "conflict",
        ErrorType.Unauthenticated => "unauthenticated",
        ErrorType.Forbidden => "forbidden",
        ErrorType.Rule => "rule-violation",
        _ => "internal-error"
    };
}

/// <summary>One field-level validation failure in a Problem Details response.</summary>
public sealed record ErrorDetail(string? Field, string Code, string Message);
