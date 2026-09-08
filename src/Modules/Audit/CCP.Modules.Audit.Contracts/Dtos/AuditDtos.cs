namespace CCP.Modules.Audit.Contracts.Dtos;

/// <summary>One audit record, as returned by search.</summary>
public sealed record AuditEventDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Application,
    string Module,
    string Action,
    string Result,
    string? ResourceType,
    string? ResourceId,
    Guid? ActorUserId,
    string? ActorUsername,
    Guid? OnBehalfOfUserId,
    string? IpAddress,
    string? CorrelationId,
    string? OldValue,
    string? NewValue,
    string? Metadata);

/// <summary>
/// What an external application posts.
/// <para>
/// <c>Application</c> is absent on purpose: it is taken from the caller's own
/// identity, never from the body. Letting a request name its own application
/// would let any holder of an ingestion credential forge another system's trail.
/// </para>
/// </summary>
public sealed record IngestAuditEventDto(
    string Module,
    string Action,
    DateTimeOffset? OccurredAt,
    string? Result,
    string? ResourceType,
    string? ResourceId,
    Guid? ActorUserId,
    string? ActorUsername,
    Guid? OnBehalfOfUserId,
    string? OldValue,
    string? NewValue,
    string? Metadata);

/// <summary>The outcome of an ingestion request.</summary>
public sealed record IngestionResultDto(int Accepted);
