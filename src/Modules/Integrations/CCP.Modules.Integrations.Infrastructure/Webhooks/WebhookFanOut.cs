using System.Text.Json;
using CCP.Kernel.Application.Events;
using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Modules.Integrations.Application.Abstractions;
using CCP.Modules.Integrations.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CCP.Modules.Integrations.Infrastructure.Webhooks;

/// <summary>
/// Turns one Platform event into one delivery per interested subscriber.
/// <para>
/// <b>A row each, not a row with a list.</b> One subscriber being down must not
/// hold up another, and a single row cannot express "delivered to two of three,
/// retrying the third" — which is the ordinary state of affairs rather than an
/// edge case.
/// </para>
/// <para>
/// <b>It queues and does not send.</b> The outbox relay is holding a transaction
/// while this runs; posting to somebody else's server from inside it would hold
/// a database transaction open for as long as their slowest endpoint takes, and
/// a subscriber that never answers would stall the relay for everybody.
/// </para>
/// </summary>
public sealed class WebhookFanOut(
    IIntegrationRepository repository,
    IIntegrationUnitOfWork unitOfWork,
    IClock clock,
    ILogger<WebhookFanOut> logger) : IIntegrationEventObserver
{
    /// <summary>
    /// Web defaults, so the JSON a subscriber receives looks like the JSON the
    /// API returns rather than like a .NET object printed out.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public async Task ObserveAsync(
        IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        IReadOnlyList<WebhookSubscription> subscribers =
            await repository.GetLiveSubscriptionsForAsync(
                integrationEvent.EventType, cancellationToken);

        if (subscribers.Count == 0)
        {
            return;
        }

        DateTimeOffset now = clock.UtcNow;

        // Serialised once for every subscriber, and stored rather than
        // recomputed later. By the time a subscriber comes back the event is
        // long gone from the outbox, and a retry that rebuilt the body would
        // send something other than what the earlier attempts claimed to.
        string payload = JsonSerializer.Serialize(
            integrationEvent, integrationEvent.GetType(), SerializerOptions);

        foreach (WebhookSubscription subscriber in subscribers)
        {
            repository.AddDelivery(WebhookDelivery.Queue(
                subscriber.Id, integrationEvent.EventId, integrationEvent.EventType, payload, now));
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicate(exception))
        {
            // The same event, fanned out twice.
            //
            // Delivery from the outbox is at-least-once, so this observer runs
            // again after a crash between sending and marking the message
            // processed. The unique index on (subscription, event) is what makes
            // the second run a no-op instead of a second round of posts to
            // everybody -- enforced by the database, because two instances can
            // reach this line at the same moment and neither read would see the
            // other's rows.
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "Webhook fan-out for {EventType} {EventId} was already recorded.",
                    integrationEvent.EventType, integrationEvent.EventId);
            }
        }
    }

    private static bool IsDuplicate(DbUpdateException exception)
        => exception.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}
