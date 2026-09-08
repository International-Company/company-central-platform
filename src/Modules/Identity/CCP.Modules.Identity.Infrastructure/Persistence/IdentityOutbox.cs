using System.Text.Json;
using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Domain;
using CCP.Kernel.Infrastructure.Outbox;
using CCP.Modules.Identity.Application.Abstractions;

namespace CCP.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Stages Identity integration events on the module's own
/// <see cref="IdentityDbContext"/>.
/// <para>
/// The row is added to the change tracker but not saved here. It is committed by
/// <see cref="IdentityUnitOfWork"/>, in the same transaction as the entity
/// changes that produced it — which is the whole guarantee the outbox exists to
/// provide (ARCHITECTURE.md §8.5).
/// </para>
/// </summary>
public sealed class IdentityOutbox(
    IdentityDbContext dbContext,
    IRequestContext requestContext) : IIdentityOutbox
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public Task EnqueueAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        Type eventType = integrationEvent.GetType();

        dbContext.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = integrationEvent.EventId,
            EventType = integrationEvent.EventType,

            // Assembly-qualified without version, so a rebuild does not orphan
            // messages that are already queued.
            PayloadType = $"{eventType.FullName}, {eventType.Assembly.GetName().Name}",
            Payload = JsonSerializer.Serialize(integrationEvent, eventType, SerializerOptions),
            OccurredAt = integrationEvent.OccurredAt,
            CorrelationId = requestContext.CorrelationId,
            NextAttemptAt = integrationEvent.OccurredAt,
            AttemptCount = 0
        });

        return Task.CompletedTask;
    }
}

/// <summary>
/// Commits the Identity module's changes.
/// <para>
/// One <c>SaveChanges</c> on one context, so entities and their staged events
/// are written in a single implicit transaction. No shared connection, no
/// distributed transaction, no coordination — the atomicity comes from them
/// living in the same context.
/// </para>
/// </summary>
public sealed class IdentityUnitOfWork(IdentityDbContext dbContext) : IIdentityUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => dbContext.SaveChangesAsync(cancellationToken);
}
