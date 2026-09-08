using CCP.Kernel.Api.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace CCP.Kernel.Api.Errors;

/// <summary>
/// The last line of defence: converts any unhandled exception into a Problem
/// Details response that reveals nothing about the internals.
/// <para>
/// The exception detail goes to the log, keyed by correlation id. The caller
/// gets only that id. This is what keeps a stack trace from reaching a
/// browser (ARCHITECTURE.md §11.3).
/// </para>
/// </summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, RequestContextAccessor requestContext)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            string correlationId = requestContext.CorrelationId;

            logger.LogError(
                exception,
                "Unhandled exception. CorrelationId={CorrelationId} Path={Path} Method={Method}",
                correlationId,
                context.Request.Path.Value,
                context.Request.Method);

            if (context.Response.HasStarted)
            {
                // The response is already on the wire; there is nothing safe to
                // do but let it fail. Logged above so it is not silent.
                throw;
            }

            ProblemDetails problem = ProblemDetailsFactory.Unexpected(
                correlationId,
                context.Request.Path.Value);

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            await context.Response.WriteAsJsonAsync(problem, context.RequestAborted);
        }
    }
}
