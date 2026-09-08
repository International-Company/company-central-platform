using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Application.Auditing;
using CCP.Kernel.Primitives;
using CCP.Modules.Audit.Application.Abstractions;
using CCP.Modules.Audit.Domain;

namespace CCP.Modules.Audit.Infrastructure;

/// <summary>
/// Implements the kernel's audit seam for the Platform.
/// <para>
/// This is the join between the two halves of §6.2: the contract lives in the
/// kernel so every module can record without referencing another module, and the
/// behaviour lives here, in the module that owns the trail. Identity and
/// Organization call <see cref="IAuditTrail"/> and never learn that an Audit
/// module exists.
/// </para>
/// <para>
/// It fills in the actor, the origin and the correlation from the ambient
/// request, so a caller supplies only what it actually knows. A module that had
/// to pass the actor on every call would eventually pass the wrong one.
/// </para>
/// </summary>
public sealed class PlatformAuditTrail(
    IAuditRecorder recorder,
    ICurrentUser currentUser,
    IRequestContext requestContext,
    IClock clock) : IAuditTrail
{
    /// <summary>
    /// Events recorded through this seam come from the Platform itself.
    /// A business application writing its own events posts to
    /// <c>/api/v1/audit/events</c> and is attributed by its own credential.
    /// </summary>
    private const string PlatformApplication = "platform";

    public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        AuditEvent auditEvent = AuditEvent.Record(
            application: PlatformApplication,
            module: entry.Module,
            action: entry.Action,
            occurredAt: clock.UtcNow,
            result: Map(entry.Outcome),
            resourceType: entry.ResourceType,
            resourceId: entry.ResourceId,
            actorUserId: currentUser.UserId,
            actorUsername: currentUser.Username,

            // Set only when a registered application is acting for a person.
            // A person signed in directly is not acting on anyone's behalf, and
            // filling this in for them would invent a delegation that never
            // happened.
            onBehalfOfUserId: currentUser.ApplicationId is null ? null : currentUser.UserId,
            ipAddress: requestContext.IpAddress,
            userAgent: requestContext.UserAgent,
            requestId: requestContext.RequestId,
            correlationId: requestContext.CorrelationId,
            oldValue: entry.OldValue,
            newValue: entry.NewValue,
            metadata: entry.Metadata);

        // The recorder swallows and logs its own failures, which is what keeps
        // the promise that an audit write never fails the operation it records.
        return recorder.RecordAsync(auditEvent, cancellationToken);
    }

    private static AuditResult Map(AuditOutcome outcome) => outcome switch
    {
        AuditOutcome.Success => AuditResult.Success,
        AuditOutcome.Failure => AuditResult.Failure,
        AuditOutcome.Denied => AuditResult.Denied,

        // An unmapped outcome is recorded as a failure rather than dropped or
        // guessed at. Losing the event would be worse than filing it slightly
        // wrong, and Success would be an actively misleading default.
        _ => AuditResult.Failure
    };
}
