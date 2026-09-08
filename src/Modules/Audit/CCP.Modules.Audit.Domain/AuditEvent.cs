using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;

namespace CCP.Modules.Audit.Domain;

/// <summary>How an audited attempt ended.</summary>
public enum AuditResult
{
    /// <summary>The action was performed.</summary>
    Success = 1,

    /// <summary>The action was attempted and failed for its own reasons.</summary>
    Failure = 2,

    /// <summary>
    /// The action was refused by authorization.
    /// <para>
    /// Kept apart from <see cref="Failure"/> deliberately. A run of denials is a
    /// person discovering the edges of their access, or someone mapping what
    /// they can reach; a run of failures is usually a broken integration. They
    /// read differently and are investigated differently.
    /// </para>
    /// </summary>
    Denied = 3
}

/// <summary>
/// One entry in the company-wide trail: who did what, to what, when, from
/// where, and what changed (ARCHITECTURE.md §15.2).
/// <para>
/// <b>Append-only, and there is no method here that changes one.</b> No setter is
/// public, nothing exposes mutation, and the database role the application uses
/// holds <c>INSERT</c> and <c>SELECT</c> on this schema and nothing else. Both
/// halves matter: code without the privilege would still be a promise, and the
/// privilege without the code discipline would be an accident waiting to be
/// made.
/// </para>
/// <para>
/// It is not an <see cref="AggregateRoot"/> and raises no domain events. An
/// audit record is a fact that has already happened; treating it as an entity
/// with a lifecycle would invite exactly the mutation this design forbids.
/// </para>
/// </summary>
public sealed class AuditEvent : Entity
{
    private AuditEvent() { }

    private AuditEvent(Guid id, DateTimeOffset occurredAt)
        : base(id)
        => OccurredAt = occurredAt;

    /// <summary>
    /// When the audited thing happened — <b>the partition key</b>.
    /// <para>
    /// The time of the event, not the time it was written. Audit writes travel
    /// through the outbox and land a moment later; recording the write time
    /// would misorder events under load and put an event in the wrong month at a
    /// boundary.
    /// </para>
    /// </summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Which system produced this: <c>platform</c>, <c>finance</c>, <c>hr</c>, …</summary>
    public string Application { get; private set; } = string.Empty;

    /// <summary>Module or subsystem within that application.</summary>
    public string Module { get; private set; } = string.Empty;

    /// <summary>The verb: <c>user.created</c>, <c>role.assigned</c>.</summary>
    public string Action { get; private set; } = string.Empty;

    public string? ResourceType { get; private set; }

    public string? ResourceId { get; private set; }

    public Guid? ActorUserId { get; private set; }

    /// <summary>
    /// The actor's username, <b>denormalized on purpose</b>.
    /// <para>
    /// A trail that renders as "user 8f3a… did X" after someone's record changes
    /// is not usable evidence. The name is captured as it stood at the time,
    /// which is also what a later reader needs to see.
    /// </para>
    /// </summary>
    public string? ActorUsername { get; private set; }

    /// <summary>Set when an application acts for a person rather than for itself.</summary>
    public Guid? OnBehalfOfUserId { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public string? DeviceId { get; private set; }

    public string? RequestId { get; private set; }

    /// <summary>Ties this event to the logs and traces of the same request.</summary>
    public string? CorrelationId { get; private set; }

    public AuditResult Result { get; private set; }

    /// <summary>JSON, already redacted. See <see cref="Redaction"/>.</summary>
    public string? OldValue { get; private set; }

    /// <summary>JSON, already redacted.</summary>
    public string? NewValue { get; private set; }

    /// <summary>JSON. Open extension for any system.</summary>
    public string? Metadata { get; private set; }

    /// <summary>
    /// Records an event.
    /// <para>
    /// <c>oldValue</c> and <c>newValue</c> are redacted here rather than by the
    /// caller. Redaction that depends on every caller remembering is redaction
    /// that will one day be forgotten, and the thing forgotten will be a
    /// password.
    /// </para>
    /// </summary>
    public static AuditEvent Record(
        string application,
        string module,
        string action,
        DateTimeOffset occurredAt,
        AuditResult result = AuditResult.Success,
        string? resourceType = null,
        string? resourceId = null,
        Guid? actorUserId = null,
        string? actorUsername = null,
        Guid? onBehalfOfUserId = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? deviceId = null,
        string? requestId = null,
        string? correlationId = null,
        string? oldValue = null,
        string? newValue = null,
        string? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(application);
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        return new AuditEvent(Uuid7.NewGuid(occurredAt), occurredAt)
        {
            Application = application.ToLowerInvariant(),
            Module = module.ToLowerInvariant(),
            Action = action.ToLowerInvariant(),
            Result = result,
            ResourceType = resourceType,
            ResourceId = resourceId,
            ActorUserId = actorUserId,
            ActorUsername = actorUsername,
            OnBehalfOfUserId = onBehalfOfUserId,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            DeviceId = deviceId,
            RequestId = requestId,
            CorrelationId = correlationId,
            OldValue = Redaction.Apply(oldValue),
            NewValue = Redaction.Apply(newValue),
            Metadata = Redaction.Apply(metadata)
        };
    }
}
