namespace CCP.Kernel.Application.Auditing;

/// <summary>How an audited attempt ended. Mirrors the Audit module's own enum.</summary>
public enum AuditOutcome
{
    Success = 1,

    Failure = 2,

    /// <summary>Refused by authorization, which reads differently from a failure.</summary>
    Denied = 3
}

/// <summary>
/// What a module says happened.
/// <para>
/// <b>Deliberately incomplete.</b> It carries only what the module knows — which
/// module, what action, on what, and what changed. Who was acting, from what
/// address, under which correlation id and on behalf of which application are
/// filled in by the implementation from the ambient request.
/// </para>
/// <para>
/// That split is the point. A module that had to supply the actor on every call
/// would eventually supply the wrong one, or none; and every call site would
/// repeat the same six lines of plumbing until somebody shortened them.
/// </para>
/// </summary>
/// <param name="Module">The module or subsystem, e.g. <c>identity</c>.</param>
/// <param name="Action">The verb, e.g. <c>user.created</c>, <c>role.assigned</c>.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="ResourceType">What was acted upon, e.g. <c>user</c>.</param>
/// <param name="ResourceId">Its identifier.</param>
/// <param name="OldValue">JSON before the change. Redacted downstream, not here.</param>
/// <param name="NewValue">JSON after the change. Redacted downstream, not here.</param>
/// <param name="Metadata">JSON. Anything else worth keeping.</param>
public sealed record AuditEntry(
    string Module,
    string Action,
    AuditOutcome Outcome = AuditOutcome.Success,
    string? ResourceType = null,
    string? ResourceId = null,
    string? OldValue = null,
    string? NewValue = null,
    string? Metadata = null);

/// <summary>
/// The seam every module writes the audit trail through.
/// <para>
/// <b>It lives in the kernel, not in the Audit module.</b> Identity, Organization,
/// Authorization and Security all have to record what they do, and none of them
/// may reference another module (ARCHITECTURE.md §6.2). Putting the contract
/// here — and the implementation in Audit — is what lets every module record
/// without any of them knowing that the Audit module exists.
/// </para>
/// <para>
/// The same shape as the neutral <c>ScopeFilter</c> and the step-up requirement:
/// the kernel owns the contract, one module owns the behaviour.
/// </para>
/// <para>
/// <b>Implementations must not throw.</b> An audit write must never fail the
/// operation it is recording — refusing a legitimate role grant because the
/// trail was briefly unwritable trades a real capability for a record nobody
/// asked to prioritise that way. A failure is logged, loudly, so that "the trail
/// is behind" is an alert rather than a silence.
/// </para>
/// </summary>
public interface IAuditTrail
{
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>
/// The trail when no Audit module is registered.
/// <para>
/// Registered by the kernel so a host that omits Audit still starts. It records
/// nothing, which is honest: the alternative is every module needing to check
/// whether auditing exists before saying anything, and that check being wrong
/// somewhere.
/// </para>
/// </summary>
public sealed class NullAuditTrail : IAuditTrail
{
    public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
