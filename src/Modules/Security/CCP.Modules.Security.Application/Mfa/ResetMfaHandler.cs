using CCP.Kernel.Application.Auditing;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Security.Application.Abstractions;
using CCP.Modules.Security.Domain;
using CCP.Modules.Security.Domain.Events;
using CCP.Modules.Security.Domain.Mfa;

namespace CCP.Modules.Security.Application.Mfa;

/// <summary>Clearing somebody else's second factor.</summary>
/// <param name="TargetUserId">Whose factor is being removed.</param>
/// <param name="ActorUserId">The administrator doing it.</param>
/// <param name="Reason">
/// Why. Required, and it is the only part of this a person can read afterwards.
/// "They lost their phone and I verified them in person" and "somebody rang up
/// claiming to be them" are the same operation and opposite acts.
/// </param>
public sealed record ResetMfaCommand(
    Guid TargetUserId,
    Guid ActorUserId,
    string ActorUsername,
    string Reason);

/// <summary>
/// The way back for somebody who has lost both their phone and their recovery
/// codes.
/// <para>
/// <b>Until now there was none.</b> Disabling a factor requires proving it, and
/// recovery codes exist precisely for the case where the phone is gone — so
/// losing both left an account permanently unusable, with the person unable to
/// sign in and nobody able to help them. That is not a security property; it is
/// a locked door with the key inside.
/// </para>
/// <para>
/// <b>This is deliberately not the self-service disable with the check removed.</b>
/// It is a separate operation, with a separate permission, a separate audit
/// action and a separate security event, because it is a separate act: one
/// person proving something about themselves, versus one person asserting
/// something about somebody else. Recording them identically would make the
/// trail unable to answer the only question worth asking afterwards — did the
/// account holder do this, or did an administrator?
/// </para>
/// <para>
/// <b>It is the most dangerous button in the Platform, and it is meant to be
/// used.</b> Anyone who can press it can strip the second factor from any
/// account, which is the first thing an attacker does after taking one. So it
/// demands a permission, and the endpoint demands recent proof of the
/// administrator's own second factor — the person removing a factor must have
/// one.
/// </para>
/// </summary>
public sealed class ResetMfaHandler(
    ISecurityRepository repository,
    ISecurityEventRecorder eventRecorder,
    ISecurityUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        ResetMfaCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure(SecurityErrors.ResetReasonRequired);
        }

        if (command.TargetUserId == command.ActorUserId)
        {
            // Resetting one's own factor through this door would be the
            // self-service disable with its proof removed -- which is the whole
            // control. Somebody who still has their factor uses the ordinary
            // path; somebody who does not needs a second person, which is the
            // point.
            return Result.Failure(SecurityErrors.CannotResetOwnMfa);
        }

        DateTimeOffset now = clock.UtcNow;

        MfaEnrolment? enrolment =
            await repository.FindActiveEnrolmentAsync(command.TargetUserId, cancellationToken);

        if (enrolment is null)
        {
            return Result.Failure(SecurityErrors.MfaNotActive);
        }

        enrolment.Disable(now);

        // Elevation granted by a factor that no longer exists must not outlive
        // it -- and here it matters more than in the self-service case, because
        // the person whose confirmations these are did not ask for any of this.
        await repository.RevokeStepUpConfirmationsAsync(command.TargetUserId, now, cancellationToken);

        // High, which is the top of the scale, and the same as a self-service
        // disable. What distinguishes the two is the event *type*, not the
        // severity: one is somebody managing their own account, the other is
        // somebody else's factor being taken away -- both a legitimate recovery
        // and the exact shape of an insider taking over an account. A shared
        // type could not tell an investigation which had happened; a shared
        // severity says only that both deserve attention, which is true.
        await eventRecorder.RecordAsync(
            SecurityEvent.Record(
                SecurityEventTypes.MfaReset,
                SecuritySeverity.High,
                now,
                command.TargetUserId,
                username: null),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The resource is the person who lost their factor, so a search of what
        // happened to that account finds it. The administrator is named in the
        // detail rather than as the resource, because the trail already records
        // who acted on every entry -- what it would not otherwise record is that
        // this particular act was done *to* somebody.
        await auditTrail.RecordAsync(
            new AuditEntry(
                "security",
                "mfa.reset-by-administrator",
                AuditOutcome.Success,
                "user",
                command.TargetUserId.ToString(),
                NewValue: $$"""
                {"resetBy":"{{command.ActorUsername}}","reason":{{Quoted(command.Reason)}}}
                """),
            cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// The reason as a JSON string, with quotes and backslashes escaped.
    /// <para>
    /// Free text somebody typed. A quote mark producing a malformed audit record
    /// is worse than an ugly one, because this record is what an investigation
    /// into a possible account takeover would be reading.
    /// </para>
    /// </summary>
    private static string Quoted(string value) =>
        $"\"{value.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
