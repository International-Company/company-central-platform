using CCP.Kernel.Application.Abstractions;

namespace CCP.Kernel.Api.Context;

/// <summary>The identifier values captured for one request.</summary>
public sealed record RequestContextValues(
    string RequestId,
    string CorrelationId,
    string? IpAddress,
    string? UserAgent);

/// <summary>
/// Holds the current request's context for the lifetime of that request.
/// <para>
/// Registered as scoped and populated by <see cref="CorrelationIdMiddleware"/>.
/// This is what allows the Application layer to read correlation identifiers
/// through <see cref="IRequestContext"/> without referencing ASP.NET Core.
/// </para>
/// </summary>
public sealed class RequestContextAccessor : IRequestContext
{
    private RequestContextValues? _values;

    public void Set(RequestContextValues values) => _values = values;

    public string RequestId => _values?.RequestId ?? "unknown";

    public string CorrelationId => _values?.CorrelationId ?? "unknown";

    public string? IpAddress => _values?.IpAddress;

    public string? UserAgent => _values?.UserAgent;
}
