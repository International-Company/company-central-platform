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
/// <b>The failing status comes from the registration, never from here.</b> That
/// is not tidiness: for three phases this returned <c>Unhealthy</c> while the
/// composition root registered <c>failureStatus: Degraded</c>, and the
/// registration loses that argument silently — <c>failureStatus</c> applies only
/// when a check throws. The Platform therefore answered 503 to readiness
/// whenever the bucket was unreachable, emptying every instance out of rotation
/// over object storage, while the code and the runbook both said it would not.
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
            // The status the *registration* asked for, not one chosen here.
            //
            // This read HealthCheckResult.Unhealthy for three phases, and it was
            // wrong in a way nothing could see. `AddCheck<T>(failureStatus:
            // Degraded)` only applies when a check throws; a check that catches
            // and returns its own result overrides the registration silently.
            // So the composition root said "a bucket being unreachable degrades
            // the Platform", the runbook said the same, and readiness answered
            // 503 -- taking every instance out of rotation over object storage,
            // which is precisely the Phase 10 outage wearing different clothes.
            //
            // Reading the registration means the decision lives in exactly one
            // place. Naming a status here at all would be a second copy of it,
            // and a second copy is what produced the defect.
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                "document storage is unreachable",
                exception: null);
        }
    }
}
