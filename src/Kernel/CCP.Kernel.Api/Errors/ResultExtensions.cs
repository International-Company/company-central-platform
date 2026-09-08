using CCP.Kernel.Api.Context;
using CCP.Kernel.Results;
using Microsoft.AspNetCore.Http;

// Our own `CCP.Kernel.Results` namespace shadows ASP.NET Core's static
// `Results` class, so the framework helper is aliased explicitly here.
using Http = Microsoft.AspNetCore.Http.Results;

namespace CCP.Kernel.Api.Errors;

/// <summary>
/// Converts a <see cref="Result"/> into an HTTP response.
/// <para>
/// Endpoints call these rather than choosing status codes themselves, which is
/// how the status code for a given failure category stays identical across
/// every module.
/// </para>
/// </summary>
public static class ResultExtensions
{
    /// <summary>Maps a value-returning result to 200, or to its error response.</summary>
    public static IResult ToHttpResult<TValue>(
        this Result<TValue> result,
        HttpContext context,
        RequestContextAccessor requestContext)
        => result.IsSuccess
            ? Http.Ok(result.Value)
            : Problem(result.Errors, context, requestContext);

    /// <summary>Maps a void result to 204, or to its error response.</summary>
    public static IResult ToHttpResult(
        this Result result,
        HttpContext context,
        RequestContextAccessor requestContext)
        => result.IsSuccess
            ? Http.NoContent()
            : Problem(result.Errors, context, requestContext);

    /// <summary>Maps a successful creation to 201 with a Location header.</summary>
    public static IResult ToCreatedResult<TValue>(
        this Result<TValue> result,
        string location,
        HttpContext context,
        RequestContextAccessor requestContext)
        => result.IsSuccess
            ? Http.Created(location, result.Value)
            : Problem(result.Errors, context, requestContext);

    private static IResult Problem(
        IReadOnlyList<Error> errors,
        HttpContext context,
        RequestContextAccessor requestContext)
    {
        var problem = ProblemDetailsFactory.Create(
            errors,
            requestContext.CorrelationId,
            context.Request.Path.Value);

        return Http.Json(
            problem,
            statusCode: problem.Status,
            contentType: "application/problem+json");
    }
}
