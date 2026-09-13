using CCP.Kernel.Application.Auditing;
using CCP.Kernel.Application.Events;
using CCP.Kernel.Paging;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Integrations.Application.Abstractions;
using CCP.Modules.Integrations.Application.Providers;
using CCP.Modules.Integrations.Contracts.Dtos;
using CCP.Modules.Integrations.Domain;
using CCP.Modules.Integrations.Domain.Outbound;
using CCP.Modules.Integrations.Domain.Webhooks;

namespace CCP.Modules.Integrations.Application.Webhooks;

// ---------------------------------------------------------------------------
// Commands and queries
// ---------------------------------------------------------------------------

/// <summary>Asking to be told when something happens.</summary>
public sealed record RegisterSubscriptionCommand(
    Guid ApplicationId,
    string Name,
    string Endpoint,
    IReadOnlyList<string> EventTypes,
    string SecretReference);

public sealed record ReconfigureSubscriptionCommand(
    Guid SubscriptionId,
    string Name,
    string Endpoint,
    IReadOnlyList<string> EventTypes,
    string SecretReference);

public sealed record SetSubscriptionEnabledCommand(Guid SubscriptionId, bool IsEnabled);

/// <summary>Bringing a suspended subscription back.</summary>
public sealed record ResumeSubscriptionCommand(Guid SubscriptionId);

public sealed record DeleteSubscriptionCommand(Guid SubscriptionId);

public sealed record GetDeliveriesQuery(Guid SubscriptionId, PageRequest Page);

// ---------------------------------------------------------------------------
// Mapping
// ---------------------------------------------------------------------------

/// <summary>
/// The shapes a subscription is published in.
/// <para>
/// The secret <i>reference</i> appears; no field here could carry a value,
/// because no field in the module holds one.
/// </para>
/// </summary>
public static class SubscriptionMapper
{
    public static WebhookSubscriptionDto ToDto(WebhookSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        return new WebhookSubscriptionDto(
            subscription.Id,
            subscription.ApplicationId,
            subscription.Name,
            subscription.Endpoint,
            subscription.EventTypes,
            subscription.SecretReference,
            subscription.IsEnabled,
            subscription.SuspendedAt,
            subscription.SuspendedReason,
            subscription.ConsecutiveFailures,
            subscription.LastDeliveredAt,
            subscription.CreatedAt);
    }

    public static WebhookDeliveryDto ToDto(WebhookDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        return new WebhookDeliveryDto(
            delivery.Id,
            delivery.EventId,
            delivery.EventType,
            delivery.Status.ToString(),
            delivery.Attempts,
            delivery.NextAttemptAt,
            delivery.ResponseStatusCode,
            delivery.LastError,
            delivery.CreatedAt,
            delivery.DeliveredAt);
    }
}

// ---------------------------------------------------------------------------
// Handlers
// ---------------------------------------------------------------------------

/// <summary>
/// Registers a subscription, after checking the Platform is allowed to call it.
/// <para>
/// <b>The address is checked now, not when an event fires.</b> A subscription
/// nobody can deliver to is a subscription whose owner believes they are being
/// told things — and the failure would arrive weeks later, in a sweep's summary,
/// where nobody is looking for it.
/// </para>
/// </summary>
public sealed class RegisterSubscriptionHandler(
    IIntegrationRepository repository,
    IIntegrationUnitOfWork unitOfWork,
    IOutboundGuard guard,
    IEventTypeCatalogue catalogue,
    IAuditTrail auditTrail,
    IClock clock)
{
    public async Task<Result<WebhookSubscriptionDto>> HandleAsync(
        RegisterSubscriptionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<WebhookSubscription> subscription = WebhookSubscription.Register(
            command.ApplicationId, command.Name, command.Endpoint,
            command.EventTypes, command.SecretReference, clock.UtcNow);

        if (subscription.IsFailure)
        {
            return Result.Failure<WebhookSubscriptionDto>(subscription.Errors);
        }

        Result known = CheckEventTypes(catalogue, subscription.Value.EventTypes);

        if (known.IsFailure)
        {
            return Result.Failure<WebhookSubscriptionDto>(known.Errors);
        }

        Result refused = await CheckAddressAsync(guard, command.Endpoint, cancellationToken);

        if (refused.IsFailure)
        {
            return Result.Failure<WebhookSubscriptionDto>(refused.Errors);
        }

        repository.AddSubscription(subscription.Value);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Audited, because a subscription is a standing instruction to send the
        // company's events to an address somebody chose. Who chose it, and when,
        // is the first question after anything goes wrong with one.
        await auditTrail.RecordAsync(
            new AuditEntry(
                IntegrationAudit.ModuleName,
                "webhook.subscription.registered",
                AuditOutcome.Success,
                "webhook-subscription",
                subscription.Value.Id.ToString(),
                NewValue: $$"""{"endpoint":"{{subscription.Value.Endpoint}}"}"""),
            cancellationToken);

        return Result.Success(SubscriptionMapper.ToDto(subscription.Value));
    }

    /// <summary>
    /// Refuses a subscription to an event type the Platform does not send.
    /// <para>
    /// Checked against the normalised list the aggregate kept, so whitespace
    /// and duplicates are already gone and the error names what was meant.
    /// The domain stays unaware of which types exist; that is the running
    /// Platform's knowledge, not the subscription's.
    /// </para>
    /// </summary>
    public static Result CheckEventTypes(
        IEventTypeCatalogue catalogue, IReadOnlyList<string> eventTypes)
    {
        string[] unknown = [.. eventTypes.Where(type => !catalogue.IsKnown(type))];

        return unknown.Length == 0
            ? Result.Success()
            : Result.Failure(IntegrationErrors.SubscriptionEventTypesUnknown(unknown));
    }

    internal static async Task<Result> CheckAddressAsync(
        IOutboundGuard guard, string endpoint, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out Uri? address))
        {
            return Result.Failure(IntegrationErrors.SubscriptionEndpointInvalid);
        }

        OutboundHostPolicy.Verdict verdict =
            await guard.InspectAsync(address, cancellationToken);

        return verdict == OutboundHostPolicy.Verdict.Allowed
            ? Result.Success()
            : Result.Failure(IntegrationErrors.SubscriptionEndpointRefused);
    }
}

public sealed class ReconfigureSubscriptionHandler(
    IIntegrationRepository repository,
    IIntegrationUnitOfWork unitOfWork,
    IOutboundGuard guard,
    IEventTypeCatalogue catalogue,
    IAuditTrail auditTrail,
    IClock clock)
{
    public async Task<Result<WebhookSubscriptionDto>> HandleAsync(
        ReconfigureSubscriptionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        WebhookSubscription? subscription =
            await repository.FindSubscriptionAsync(command.SubscriptionId, cancellationToken);

        if (subscription is null)
        {
            return Result.Failure<WebhookSubscriptionDto>(IntegrationErrors.SubscriptionNotFound);
        }

        string previous = subscription.Endpoint;

        Result changed = subscription.Reconfigure(
            command.Name, command.Endpoint, command.EventTypes,
            command.SecretReference, clock.UtcNow);

        if (changed.IsFailure)
        {
            return Result.Failure<WebhookSubscriptionDto>(changed.Errors);
        }

        Result known = RegisterSubscriptionHandler.CheckEventTypes(
            catalogue, subscription.EventTypes);

        if (known.IsFailure)
        {
            return Result.Failure<WebhookSubscriptionDto>(known.Errors);
        }

        Result refused = await RegisterSubscriptionHandler.CheckAddressAsync(
            guard, command.Endpoint, cancellationToken);

        if (refused.IsFailure)
        {
            return Result.Failure<WebhookSubscriptionDto>(refused.Errors);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                IntegrationAudit.ModuleName,
                "webhook.subscription.reconfigured",
                AuditOutcome.Success,
                "webhook-subscription",
                subscription.Id.ToString(),
                OldValue: $$"""{"endpoint":"{{previous}}"}""",
                NewValue: $$"""{"endpoint":"{{subscription.Endpoint}}"}"""),
            cancellationToken);

        return Result.Success(SubscriptionMapper.ToDto(subscription));
    }
}

public sealed class SetSubscriptionEnabledHandler(
    IIntegrationRepository repository,
    IIntegrationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        SetSubscriptionEnabledCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        WebhookSubscription? subscription =
            await repository.FindSubscriptionAsync(command.SubscriptionId, cancellationToken);

        if (subscription is null)
        {
            return Result.Failure(IntegrationErrors.SubscriptionNotFound);
        }

        subscription.SetEnabled(command.IsEnabled, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>
/// Brings a suspended subscription back, and clears what suspended it.
/// <para>
/// Clearing the count is the point. Resuming with the failures still recorded
/// would suspend it again on the next failure, which looks to its owner like
/// resuming did nothing at all.
/// </para>
/// </summary>
public sealed class ResumeSubscriptionHandler(
    IIntegrationRepository repository,
    IIntegrationUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        ResumeSubscriptionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        WebhookSubscription? subscription =
            await repository.FindSubscriptionAsync(command.SubscriptionId, cancellationToken);

        if (subscription is null)
        {
            return Result.Failure(IntegrationErrors.SubscriptionNotFound);
        }

        subscription.Resume(clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                IntegrationAudit.ModuleName,
                "webhook.subscription.resumed",
                AuditOutcome.Success,
                "webhook-subscription",
                subscription.Id.ToString()),
            cancellationToken);

        return Result.Success();
    }
}

public sealed class DeleteSubscriptionHandler(
    IIntegrationRepository repository,
    IIntegrationUnitOfWork unitOfWork,
    IAuditTrail auditTrail)
{
    public async Task<Result> HandleAsync(
        DeleteSubscriptionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        WebhookSubscription? subscription =
            await repository.FindSubscriptionAsync(command.SubscriptionId, cancellationToken);

        if (subscription is null)
        {
            return Result.Failure(IntegrationErrors.SubscriptionNotFound);
        }

        repository.RemoveSubscription(subscription);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                IntegrationAudit.ModuleName,
                "webhook.subscription.deleted",
                AuditOutcome.Success,
                "webhook-subscription",
                subscription.Id.ToString(),
                OldValue: $$"""{"endpoint":"{{subscription.Endpoint}}"}"""),
            cancellationToken);

        return Result.Success();
    }
}

public sealed class GetSubscriptionsHandler(IIntegrationRepository repository)
{
    public async Task<Result<IReadOnlyList<WebhookSubscriptionDto>>> HandleAsync(
        Guid? applicationId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<WebhookSubscription> subscriptions =
            await repository.GetSubscriptionsAsync(applicationId, cancellationToken);

        return Result.Success<IReadOnlyList<WebhookSubscriptionDto>>(
            [.. subscriptions.Select(SubscriptionMapper.ToDto)]);
    }
}

/// <summary>
/// What happened to the events this subscription was meant to receive.
/// <para>
/// The answer to "did you send it?", which is the first thing asked when a
/// business system's state disagrees with the Platform's. A delivery mechanism
/// that kept no record could only answer with an opinion.
/// </para>
/// </summary>
public sealed class GetDeliveriesHandler(IIntegrationRepository repository)
{
    public async Task<Result<PagedResult<WebhookDeliveryDto>>> HandleAsync(
        GetDeliveriesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        WebhookSubscription? subscription =
            await repository.FindSubscriptionAsync(query.SubscriptionId, cancellationToken);

        if (subscription is null)
        {
            return Result.Failure<PagedResult<WebhookDeliveryDto>>(
                IntegrationErrors.SubscriptionNotFound);
        }

        (IReadOnlyList<WebhookDelivery> items, long total) =
            await repository.SearchDeliveriesAsync(
                query.SubscriptionId, query.Page.Skip, query.Page.PageSize, cancellationToken);

        return Result.Success(new PagedResult<WebhookDeliveryDto>(
            [.. items.Select(SubscriptionMapper.ToDto)],
            query.Page.Page,
            query.Page.PageSize,
            total));
    }
}
