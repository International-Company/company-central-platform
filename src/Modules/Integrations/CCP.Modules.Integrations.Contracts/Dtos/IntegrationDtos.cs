namespace CCP.Modules.Integrations.Contracts.Dtos;

/// <summary>
/// A registered provider, as an administrator sees it.
/// <para>
/// <c>CredentialReference</c> is the <i>name</i> of a secret and is safe to
/// show; there is no field here, and no field anywhere in this module, carrying
/// a secret's value.
/// </para>
/// </summary>
public sealed record IntegrationProviderDto(
    Guid Id,
    string Code,
    string Name,
    string BaseAddress,
    string? CredentialReference,
    bool IsEnabled,
    bool HasCredential,
    int TimeoutSeconds,
    int MaxRetries,
    int FailuresBeforeBreaking,
    int BreakDurationSeconds,
    int MaxConcurrentCalls,
    IReadOnlyList<string> RedactedFields,
    IReadOnlyList<IntegrationEndpointDto> Endpoints,
    DateTimeOffset CreatedAt);

/// <summary>One operation a provider offers.</summary>
public sealed record IntegrationEndpointDto(
    Guid Id,
    string Key,
    string Method,
    string PathTemplate);

/// <summary>
/// One call, as recorded.
/// <para>
/// The payloads here have already had the provider's declared sensitive fields
/// blanked — before storage, so what is shown is what is stored.
/// </para>
/// </summary>
public sealed record IntegrationCallDto(
    Guid Id,
    string ProviderCode,
    string EndpointKey,
    string Method,
    string Path,
    string CorrelationId,
    string RequestPayload,
    string ResponsePayload,
    int? StatusCode,
    string Outcome,
    string? FailureReason,
    int Attempts,
    int DurationMs,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);

/// <summary>
/// How a provider is doing, from its recent calls.
/// <para>
/// Derived rather than stored. A stored health flag is a field that goes stale
/// the moment somebody forgets to update it, and the recent calls are the
/// evidence anyway (§19.6).
/// </para>
/// </summary>
public sealed record IntegrationHealthDto(
    string ProviderCode,
    string Name,
    bool IsEnabled,
    int RecentCalls,
    int RecentFailures,
    DateTimeOffset? LastCallAt,
    DateTimeOffset? LastSuccessAt,
    string Status);
