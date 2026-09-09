using CCP.Modules.Documents.Application.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CCP.Api.Host.Configuration;

/// <summary>
/// Whether document storage can actually be reached.
/// <para>
/// <b>A readiness check that only asks about the database is a readiness check
/// that lies</b> (§22.4). The Platform can serve most requests with object
/// storage unreachable, and it cannot serve any request that touches a document
/// — so a failing store should route traffic away rather than have the Platform
/// accept uploads it will drop.
/// </para>
/// <para>
/// It <b>reads</b> rather than writes. A health check that stored an object
/// would fill the bucket with probes at whatever interval the platform polls,
/// and the read exercises the same credentials and the same network path. Asking
/// for a key nothing will ever have is expected to answer "not there", and
/// "not there" is a healthy answer — the unhealthy one is an exception.
/// </para>
/// </summary>
public sealed class DocumentStorageHealthCheck(IDocumentStorageProvider storage) : IHealthCheck
{
    /// <summary>
    /// A key nothing can hold. Twenty hex characters would be an object key;
    /// this is deliberately not one, so it can never collide with a real
    /// document even by accident.
    /// </summary>
    private const string ProbeKey = "health/probe-that-does-not-exist";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using Stream? found = await storage.OpenAsync(ProbeKey, cancellationToken);

            // Null is the expected answer and a healthy one: the store was
            // reached, understood the request, and said the object is not there.
            return HealthCheckResult.Healthy(
                found is null ? "reachable" : "reachable, and unexpectedly holding the probe key");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The message, not the exception. A health endpoint's body reaches
            // whoever can call it, and a stack trace there names internals.
            return HealthCheckResult.Unhealthy("document storage is unreachable");
        }
    }
}
