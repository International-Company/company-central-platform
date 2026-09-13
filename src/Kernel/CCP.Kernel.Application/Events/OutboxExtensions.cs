using CCP.Kernel.Domain;

namespace CCP.Kernel.Application.Events;

/// <summary>
/// Moves the integration events an aggregate raised into its module's outbox.
/// </summary>
public static class OutboxExtensions
{
    /// <summary>
    /// Stages every integration event the aggregate has raised, then clears them.
    /// <para>
    /// Call it after the domain method and before the unit of work saves, so the
    /// events commit in the same transaction as the change they describe.
    /// </para>
    /// <para>
    /// <b>Nothing does this automatically, and for most of the Platform's life
    /// that was not known.</b> <c>Entity</c> said its events were "collected by
    /// the unit of work"; no unit of work collected anything. Organization had
    /// written its own loop and published correctly. Authorization and Identity
    /// had not, so a role gaining or losing a permission and an account being
    /// locked out raised events that were silently discarded — and a business
    /// system subscribed to them by webhook waited for deliveries that could not
    /// come. <c>IntegrationEventPublicationTests</c> now fails the build on a
    /// module that raises events and never calls this.
    /// </para>
    /// </summary>
    public static async Task EnqueueRaisedEventsAsync(
        this IOutbox outbox,
        AggregateRoot aggregate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(aggregate);

        foreach (IDomainEvent domainEvent in aggregate.DomainEvents)
        {
            if (domainEvent is IIntegrationEvent integrationEvent)
            {
                await outbox.EnqueueAsync(integrationEvent, cancellationToken);
            }
        }

        aggregate.ClearDomainEvents();
    }
}
