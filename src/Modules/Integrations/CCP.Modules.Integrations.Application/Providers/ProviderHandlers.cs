using CCP.Kernel.Application.Auditing;
using CCP.Kernel.Paging;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Integrations.Application.Abstractions;
using CCP.Modules.Integrations.Contracts.Dtos;
using CCP.Modules.Integrations.Domain;
using CCP.Modules.Integrations.Domain.Logging;
using CCP.Modules.Integrations.Domain.Providers;

namespace CCP.Modules.Integrations.Application.Providers;

/// <summary>The module name every audit entry here carries.</summary>
internal static class IntegrationAudit
{
    public const string ModuleName = "integrations";
}

// ---------------------------------------------------------------------------
// Commands and queries
// ---------------------------------------------------------------------------

/// <summary>Registering an external service the Platform may call.</summary>
public sealed record RegisterProviderCommand(string Code, string Name, string BaseAddress);

/// <summary>How patient to be with a provider, and when to stop trying.</summary>
public sealed record ConfigureProviderCommand(
    Guid ProviderId,
    int TimeoutSeconds,
    int MaxRetries,
    int FailuresBeforeBreaking,
    int BreakDurationSeconds,
    int MaxConcurrentCalls,
    IReadOnlyList<string> RedactedFields,
    string? CredentialReference);

public sealed record SetProviderEnabledCommand(Guid ProviderId, bool IsEnabled);

/// <summary>Adding an operation to a provider.</summary>
public sealed record AddEndpointCommand(
    Guid ProviderId, string Key, string Method, string PathTemplate);

/// <summary>What the Platform sent, and what came back.</summary>
public sealed record SearchCallLogQuery(
    string? ProviderCode, CallOutcome? Outcome, PageRequest Page);

// ---------------------------------------------------------------------------
// Mapping
// ---------------------------------------------------------------------------

/// <summary>
/// Turns the module's types into the shapes it publishes.
/// <para>
/// Hand-written, one direction, and there is no shape here that could carry a
/// credential — because there is no field anywhere in the module holding one.
/// </para>
/// </summary>
public static class IntegrationMapper
{
    public static IntegrationProviderDto ToDto(IntegrationProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return new IntegrationProviderDto(
            provider.Id,
            provider.Code,
            provider.Name,
            provider.BaseAddress,
            provider.CredentialReference,
            provider.IsEnabled,
            provider.CredentialReference is not null,
            (int)provider.Timeout.TotalSeconds,
            provider.MaxRetries,
            provider.FailuresBeforeBreaking,
            (int)provider.BreakDuration.TotalSeconds,
            provider.MaxConcurrentCalls,
            provider.RedactionPolicy,
            [.. provider.Endpoints.Select(ToDto)],
            provider.CreatedAt);
    }

    public static IntegrationEndpointDto ToDto(IntegrationEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return new IntegrationEndpointDto(
            endpoint.Id, endpoint.Key, endpoint.Method, endpoint.PathTemplate);
    }

    public static IntegrationCallDto ToDto(IntegrationCallLog entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new IntegrationCallDto(
            entry.Id,
            entry.ProviderCode,
            entry.EndpointKey,
            entry.Method,
            entry.Path,
            entry.CorrelationId,
            entry.RequestPayload,
            entry.ResponsePayload,
            entry.StatusCode,
            entry.Outcome.ToString(),
            entry.FailureReason,
            entry.Attempts,
            entry.DurationMs,
            entry.StartedAt,
            entry.CompletedAt);
    }
}

// ---------------------------------------------------------------------------
// Handlers
// ---------------------------------------------------------------------------

/// <summary>Registers a service the Platform is allowed to talk to.</summary>
public sealed class RegisterProviderHandler(
    IIntegrationRepository repository,
    IAuditTrail auditTrail,
    IIntegrationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<IntegrationProviderDto>> HandleAsync(
        RegisterProviderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        IntegrationProvider? existing =
            await repository.FindProviderByCodeAsync(command.Code, cancellationToken);

        if (existing is not null)
        {
            return Result.Failure<IntegrationProviderDto>(IntegrationErrors.ProviderCodeTaken);
        }

        Result<IntegrationProvider> created = IntegrationProvider.Create(
            command.Code, command.Name, command.BaseAddress, clock.UtcNow);

        if (created.IsFailure)
        {
            return Result.Failure<IntegrationProviderDto>(created.Errors);
        }

        repository.AddProvider(created.Value);

        await auditTrail.RecordAsync(
            new AuditEntry(
                IntegrationAudit.ModuleName,
                "provider.registered",
                AuditOutcome.Success,
                "provider",
                created.Value.Id.ToString(),
                NewValue: $$"""{"code":"{{created.Value.Code}}","host":"{{new Uri(created.Value.BaseAddress).Host}}"}"""),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(IntegrationMapper.ToDto(created.Value));
    }
}

/// <summary>
/// Sets a provider's resilience, its redaction policy and its credential
/// reference.
/// <para>
/// All three together because they are one editing session in the screen, and
/// splitting them would mean three endpoints, three audit entries and three
/// chances to leave a provider half-configured.
/// </para>
/// </summary>
public sealed class ConfigureProviderHandler(
    IIntegrationRepository repository,
    IAuditTrail auditTrail,
    IIntegrationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<IntegrationProviderDto>> HandleAsync(
        ConfigureProviderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        IntegrationProvider? provider =
            await repository.FindProviderAsync(command.ProviderId, cancellationToken);

        if (provider is null)
        {
            return Result.Failure<IntegrationProviderDto>(IntegrationErrors.ProviderNotFound);
        }

        DateTimeOffset now = clock.UtcNow;

        Result configured = provider.ConfigureResilience(
            TimeSpan.FromSeconds(command.TimeoutSeconds),
            command.MaxRetries,
            command.FailuresBeforeBreaking,
            TimeSpan.FromSeconds(command.BreakDurationSeconds),
            command.MaxConcurrentCalls,
            now);

        if (configured.IsFailure)
        {
            return Result.Failure<IntegrationProviderDto>(configured.Errors);
        }

        Result credential = provider.SetCredentialReference(command.CredentialReference, now);

        if (credential.IsFailure)
        {
            return Result.Failure<IntegrationProviderDto>(credential.Errors);
        }

        provider.SetRedactionPolicy(command.RedactedFields, now);

        // The reference is recorded; a value could not be, because the domain
        // refuses one and there is no column to put it in.
        await auditTrail.RecordAsync(
            new AuditEntry(
                IntegrationAudit.ModuleName,
                "provider.configured",
                AuditOutcome.Success,
                "provider",
                provider.Id.ToString(),
                NewValue: $$"""{"code":"{{provider.Code}}","credentialReference":"{{provider.CredentialReference}}"}"""),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(IntegrationMapper.ToDto(provider));
    }
}

/// <summary>
/// The switch somebody pulls at three in the morning.
/// <para>
/// Disabling stops calls at the connector rather than at the caller, so every
/// module that was using this provider degrades in the same way at the same
/// moment — and the call log records each refusal, because "we did not call
/// them" answers a question that an absence of rows does not.
/// </para>
/// </summary>
public sealed class SetProviderEnabledHandler(
    IIntegrationRepository repository,
    IAuditTrail auditTrail,
    IIntegrationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        SetProviderEnabledCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        IntegrationProvider? provider =
            await repository.FindProviderAsync(command.ProviderId, cancellationToken);

        if (provider is null)
        {
            return Result.Failure(IntegrationErrors.ProviderNotFound);
        }

        provider.SetEnabled(command.IsEnabled, clock.UtcNow);

        await auditTrail.RecordAsync(
            new AuditEntry(
                IntegrationAudit.ModuleName,
                command.IsEnabled ? "provider.enabled" : "provider.disabled",
                AuditOutcome.Success,
                "provider",
                provider.Id.ToString(),
                NewValue: $$"""{"code":"{{provider.Code}}"}"""),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Adds an operation to a provider.</summary>
public sealed class AddEndpointHandler(
    IIntegrationRepository repository,
    IIntegrationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<IntegrationEndpointDto>> HandleAsync(
        AddEndpointCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        IntegrationProvider? provider =
            await repository.FindProviderAsync(command.ProviderId, cancellationToken);

        if (provider is null)
        {
            return Result.Failure<IntegrationEndpointDto>(IntegrationErrors.ProviderNotFound);
        }

        Result<IntegrationEndpoint> endpoint = provider.AddEndpoint(
            command.Key, command.Method, command.PathTemplate, clock.UtcNow);

        if (endpoint.IsFailure)
        {
            return Result.Failure<IntegrationEndpointDto>(endpoint.Errors);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(IntegrationMapper.ToDto(endpoint.Value));
    }
}

/// <summary>Every registered provider.</summary>
public sealed class GetProvidersHandler(IIntegrationRepository repository)
{
    public async Task<Result<IReadOnlyList<IntegrationProviderDto>>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IntegrationProvider> providers =
            await repository.GetProvidersAsync(cancellationToken);

        return Result.Success<IReadOnlyList<IntegrationProviderDto>>(
            [.. providers.Select(IntegrationMapper.ToDto)]);
    }
}

/// <summary>
/// What the Platform sent, and what came back.
/// <para>
/// The payloads shown here were redacted before they were stored, so what an
/// administrator reads is what the database holds — there is no unredacted copy
/// anywhere to be found by the next export.
/// </para>
/// </summary>
public sealed class SearchCallLogHandler(IIntegrationRepository repository)
{
    public async Task<Result<PagedResult<IntegrationCallDto>>> HandleAsync(
        SearchCallLogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        (IReadOnlyList<IntegrationCallLog> items, long total) =
            await repository.SearchCallLogAsync(
                query.ProviderCode, query.Outcome, query.Page, cancellationToken);

        return Result.Success(new PagedResult<IntegrationCallDto>(
            [.. items.Select(IntegrationMapper.ToDto)],
            query.Page.Page, query.Page.PageSize, total));
    }
}

/// <summary>
/// How each provider is doing, from its own recent calls.
/// <para>
/// <b>Derived, never stored.</b> A health column is a field that goes stale the
/// moment somebody forgets to update it — and the recent calls are the evidence
/// anyway, so storing a summary of them would be storing a second version of
/// something already true (§19.6).
/// </para>
/// </summary>
public sealed class GetProviderHealthHandler(IIntegrationRepository repository, IClock clock)
{
    /// <summary>How far back "recent" reaches.</summary>
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    /// <summary>
    /// How many recent calls it takes before failures mean anything.
    /// <para>
    /// One failure out of one call is not a degraded provider, it is a call that
    /// failed. Calling it degraded would make the screen cry wolf, and a screen
    /// that cries wolf is a screen nobody looks at during the outage.
    /// </para>
    /// </summary>
    private const int MeaningfulSample = 3;

    public async Task<Result<IReadOnlyList<IntegrationHealthDto>>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IntegrationProvider> providers =
            await repository.GetProvidersAsync(cancellationToken);

        DateTimeOffset since = clock.UtcNow - Window;
        var health = new List<IntegrationHealthDto>(providers.Count);

        foreach (IntegrationProvider provider in providers)
        {
            Result<PageRequest> page = PageRequest.Create(1, PageRequest.MaxPageSize);

            (IReadOnlyList<IntegrationCallLog> recent, _) = await repository.SearchCallLogAsync(
                provider.Code, outcome: null, page.Value, cancellationToken);

            IReadOnlyList<IntegrationCallLog> inWindow =
                [.. recent.Where(c => c.StartedAt >= since)];

            int failures = inWindow.Count(c => c.Outcome is not CallOutcome.Succeeded);

            health.Add(new IntegrationHealthDto(
                provider.Code,
                provider.Name,
                provider.IsEnabled,
                inWindow.Count,
                failures,
                recent.Count > 0 ? recent[0].StartedAt : null,
                recent.FirstOrDefault(c => c.Outcome == CallOutcome.Succeeded)?.StartedAt,
                Describe(provider.IsEnabled, inWindow.Count, failures)));
        }

        return Result.Success<IReadOnlyList<IntegrationHealthDto>>(health);
    }

    private static string Describe(bool isEnabled, int calls, int failures)
    {
        if (!isEnabled)
        {
            return "Disabled";
        }

        if (calls < MeaningfulSample)
        {
            // Not "healthy". Nothing has been asked of it, so nothing is known —
            // and a green light nobody earned is worse than an honest blank.
            return "Idle";
        }

        return failures == 0 ? "Healthy" : failures >= calls / 2 ? "Failing" : "Degraded";
    }
}
