using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Primitives;
using Microsoft.AspNetCore.Http;

namespace CCP.Kernel.Api.Context;

/// <summary>
/// Establishes the request and correlation identifiers.
/// <para>
/// This runs before anything that can fail, so every error — including one
/// thrown by the next middleware — is traceable (ARCHITECTURE.md §8.3).
/// </para>
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string RequestHeader = "X-Request-Id";

    private const int MaxHeaderLength = 128;

    public async Task InvokeAsync(HttpContext context, RequestContextAccessor accessor)
    {
        string correlationId = ReadInboundOrGenerate(context);
        string requestId = context.TraceIdentifier;

        accessor.Set(new RequestContextValues(
            RequestId: requestId,
            CorrelationId: correlationId,
            IpAddress: context.Connection.RemoteIpAddress?.ToString(),
            UserAgent: Truncate(context.Request.Headers.UserAgent.ToString(), 512)));

        // Echo both so a caller can correlate their side with ours.
        context.Response.Headers[CorrelationHeader] = correlationId;
        context.Response.Headers[RequestHeader] = requestId;

        await next(context);
    }

    /// <summary>
    /// Accepts a caller-supplied correlation id so an operation spanning a
    /// business application and the Platform shares one identifier. The value
    /// is sanitised: it is attacker-controlled and ends up in log files.
    /// </summary>
    private static string ReadInboundOrGenerate(HttpContext context)
    {
        string? inbound = context.Request.Headers[CorrelationHeader].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(inbound))
        {
            return Uuid7.NewGuid().ToString("N");
        }

        Span<char> buffer = stackalloc char[MaxHeaderLength];
        int length = 0;

        foreach (char c in inbound)
        {
            if (length == MaxHeaderLength)
            {
                break;
            }

            // Allow-list rather than deny-list: anything not plainly safe is
            // dropped, which prevents log forging via newline injection.
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
            {
                buffer[length++] = c;
            }
        }

        return length == 0 ? Uuid7.NewGuid().ToString("N") : new string(buffer[..length]);
    }

    private static string? Truncate(string? value, int maxLength)
        => string.IsNullOrEmpty(value) ? null
         : value.Length <= maxLength ? value
         : value[..maxLength];
}
