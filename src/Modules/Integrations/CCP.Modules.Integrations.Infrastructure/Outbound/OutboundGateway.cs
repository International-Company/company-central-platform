using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Primitives;
using CCP.Modules.Integrations.Application.Abstractions;
using CCP.Modules.Integrations.Contracts;
using CCP.Modules.Integrations.Domain.Logging;
using CCP.Modules.Integrations.Domain.Outbound;

namespace CCP.Modules.Integrations.Infrastructure.Outbound;

/// <summary>
/// Offers the governed door's protocol-independent half to channels that do not
/// speak HTTP.
/// <para>
/// The allow-list is a question about a host name and the call log is a row.
/// Neither cares what happens on the socket afterwards, so neither had any
/// business being unavailable to the email channel for nine phases.
/// </para>
/// </summary>
public sealed class OutboundGateway(
    IOutboundGuard guard,
    IIntegrationRepository repository,
    IIntegrationUnitOfWork unitOfWork,
    IRequestContext requestContext,
    IClock clock) : IOutboundGateway
{
    public async Task<OutboundApproval> ApproveHostAsync(
        string host, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        // Asked through the same guard, which means the same allow-list, the
        // same private-address refusals, and the same reasoning about why a name
        // resolving to 127.0.0.1 is not somewhere the Platform goes.
        //
        // A scheme is required to ask, and the answer does not depend on it: the
        // policy's scheme check is about http versus file, and there is no
        // SMTP-shaped URL to hand it.
        OutboundRoute route = await guard.ApproveAsync(host, cancellationToken);

        return route.IsAllowed
            ? new OutboundApproval(true, "Allowed")
            : new OutboundApproval(false, route.Verdict.ToString());
    }

    /// <summary>
    /// Writes the attempt as a call-log row, so one query answers "what has the
    /// Platform been sending" across every channel.
    /// <para>
    /// In its own save. The caller is a notification channel in the middle of a
    /// dispatch, and a log row that rolled back with a failed send would erase
    /// the evidence of exactly the failure somebody is looking for.
    /// </para>
    /// </summary>
    public async Task RecordAttemptAsync(
        OutboundAttempt attempt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        DateTimeOffset now = clock.UtcNow;
        DateTimeOffset started = now - TimeSpan.FromMilliseconds(attempt.DurationMs);

        // No provider row, and no foreign key pretending there is one. A mail
        // server is not registered the way an HTTP provider is -- it has no
        // endpoints, no path templates and no credential reference -- so the
        // code stands on its own and the id is empty rather than invented.
        IntegrationCallLog log = IntegrationCallLog.Start(
            Guid.Empty,
            attempt.Channel,
            attempt.Operation,
            "SEND",
            attempt.Destination,
            requestContext.CorrelationId,
            string.Empty,
            started);

        if (attempt.Succeeded)
        {
            log.Complete(200, string.Empty, attempts: 1, now);
        }
        else
        {
            log.Fail(CallOutcome.Failed, attempt.Detail ?? "Failed.", attempts: 1, now);
        }

        repository.AddCallLog(log);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
