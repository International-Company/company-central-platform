using System.Text.Json;
using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Domain;
using CCP.Kernel.Infrastructure.Outbox;
using CCP.Kernel.Paging;
using CCP.Modules.Integrations.Application.Abstractions;
using CCP.Modules.Integrations.Domain.Logging;
using CCP.Modules.Integrations.Domain.Providers;
using CCP.Modules.Integrations.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CCP.Modules.Integrations.Infrastructure.Persistence;

/// <summary>Reads and writes the integrations schema.</summary>
public sealed class IntegrationRepository(IntegrationDbContext dbContext) : IIntegrationRepository
{
    public async Task<IntegrationProvider?> FindProviderAsync(
        Guid providerId, CancellationToken cancellationToken = default)
        => await dbContext.Providers
            .FirstOrDefaultAsync(p => p.Id == providerId, cancellationToken);

    public async Task<IntegrationProvider?> FindProviderByCodeAsync(
        string code, CancellationToken cancellationToken = default)
    {
        string normalised = code.Trim().ToLowerInvariant();

        return await dbContext.Providers
            .FirstOrDefaultAsync(p => p.Code == normalised, cancellationToken);
    }

    public async Task<IReadOnlyList<IntegrationProvider>> GetProvidersAsync(
        CancellationToken cancellationToken = default)
        => await dbContext.Providers
            .AsNoTracking()
            .OrderBy(p => p.Code)
            .ToListAsync(cancellationToken);

    public void AddProvider(IntegrationProvider provider) => dbContext.Providers.Add(provider);

    public void AddCallLog(IntegrationCallLog entry) => dbContext.CallLog.Add(entry);

    public async Task<(IReadOnlyList<IntegrationCallLog> Items, long Total)> SearchCallLogAsync(
        string? providerCode,
        CallOutcome? outcome,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        IQueryable<IntegrationCallLog> query = dbContext.CallLog.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(providerCode))
        {
            string code = providerCode.Trim().ToLowerInvariant();

            query = query.Where(e => e.ProviderCode == code);
        }

        if (outcome is { } wanted)
        {
            query = query.Where(e => e.Outcome == wanted);
        }

        long total = await query.LongCountAsync(cancellationToken);

        List<IntegrationCallLog> items = await query
            .OrderByDescending(e => e.StartedAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<bool> HasSeenWebhookAsync(
        string providerCode, string signature, CancellationToken cancellationToken = default)
        => await dbContext.WebhookReceipts
            .AsNoTracking()
            .AnyAsync(
                r => r.ProviderCode == providerCode && r.Signature == signature,
                cancellationToken);

    public void AddWebhookReceipt(WebhookReceipt receipt)
        => dbContext.WebhookReceipts.Add(receipt);

    /// <summary>
    /// Removes what is past its retention, in bounded batches.
    /// <para>
    /// Bounded because the first sweep on a table nobody has been pruning would
    /// otherwise be one enormous delete holding locks on the busiest table in
    /// the module. Several small passes finish later and never block anybody.
    /// </para>
    /// </summary>
    public async Task<(int Calls, int Receipts)> PurgeExpiredAsync(
        DateTimeOffset callsBefore,
        DateTimeOffset receiptsBefore,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        int calls = await dbContext.CallLog
            .Where(e => e.StartedAt < callsBefore)
            .OrderBy(e => e.StartedAt)
            .Take(batchSize)
            .ExecuteDeleteAsync(cancellationToken);

        // Receipts are swept far more aggressively than the call log: nothing
        // older than the replay window can be replayed, so keeping one is
        // storing a row to answer a question the clock has already answered.
        int receipts = await dbContext.WebhookReceipts
            .Where(r => r.ReceivedAt < receiptsBefore)
            .ExecuteDeleteAsync(cancellationToken);

        return (calls, receipts);
    }

    // --- Outbound webhooks --------------------------------------------------

    public async Task<WebhookSubscription?> FindSubscriptionAsync(
        Guid subscriptionId, CancellationToken cancellationToken = default)
        => await dbContext.WebhookSubscriptions
            .FirstOrDefaultAsync(s => s.Id == subscriptionId, cancellationToken);

    public async Task<IReadOnlyList<WebhookSubscription>> GetSubscriptionsAsync(
        Guid? applicationId, CancellationToken cancellationToken = default)
        => await dbContext.WebhookSubscriptions
            .AsNoTracking()
            .Where(s => applicationId == null || s.ApplicationId == applicationId)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Live subscriptions only, filtered on the event type in the database.
    /// <para>
    /// The membership test runs in PostgreSQL rather than in memory. Reading
    /// every subscription on every event and filtering here would work for a
    /// handful and quietly become the most expensive thing the Platform does.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<WebhookSubscription>> GetLiveSubscriptionsForAsync(
        string eventType, CancellationToken cancellationToken = default)
        => await dbContext.WebhookSubscriptions
            .AsNoTracking()
            .Where(s => s.IsEnabled
                        && s.SuspendedAt == null
                        && s.EventTypes.Contains(eventType))
            .ToListAsync(cancellationToken);

    public void AddSubscription(WebhookSubscription subscription)
        => dbContext.WebhookSubscriptions.Add(subscription);

    public void RemoveSubscription(WebhookSubscription subscription)
        => dbContext.WebhookSubscriptions.Remove(subscription);

    public void AddDelivery(WebhookDelivery delivery)
        => dbContext.WebhookDeliveries.Add(delivery);

    /// <summary>
    /// Claims a batch with <c>FOR UPDATE SKIP LOCKED</c>.
    /// <para>
    /// The same mechanism the outbox relay uses, for the same reason: two
    /// instances sweeping at the same moment take different rows instead of both
    /// taking the same one. Without it, every delivery on a two-instance
    /// deployment is posted twice on every pass -- which is not what
    /// at-least-once is supposed to mean.
    /// </para>
    /// <para>
    /// The lock is held by the transaction the caller commits, so the rows stay
    /// claimed for exactly as long as this pass takes.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<WebhookDelivery>> ClaimDueDeliveriesAsync(
        DateTimeOffset asOf, int batchSize, CancellationToken cancellationToken = default)
        => await dbContext.WebhookDeliveries
            .FromSql($"""
                SELECT * FROM integrations.webhook_deliveries
                WHERE status = 1 AND next_attempt_at <= {asOf}
                ORDER BY next_attempt_at
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<WebhookDelivery> Items, long Total)> SearchDeliveriesAsync(
        Guid subscriptionId, int skip, int take, CancellationToken cancellationToken = default)
    {
        IQueryable<WebhookDelivery> query = dbContext.WebhookDeliveries
            .AsNoTracking()
            .Where(d => d.SubscriptionId == subscriptionId);

        long total = await query.LongCountAsync(cancellationToken);

        List<WebhookDelivery> items = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (items, total);
    }
}

/// <summary>Commits the Integrations module's changes.</summary>
public sealed class IntegrationUnitOfWork(IntegrationDbContext dbContext) : IIntegrationUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => dbContext.SaveChangesAsync(cancellationToken);
}

/// <summary>The module's slice of the transactional outbox.</summary>
public sealed class IntegrationOutbox(
    IntegrationDbContext dbContext, IRequestContext requestContext) : IIntegrationOutbox
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public Task EnqueueAsync(
        IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        Type eventType = integrationEvent.GetType();

        dbContext.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = integrationEvent.EventId,
            EventType = integrationEvent.EventType,
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

/// <summary>Builds the context for design-time tooling.</summary>
public sealed class IntegrationDbContextFactory : IDesignTimeDbContextFactory<IntegrationDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=ccp_design_time;Username=design;Password=design";

    public IntegrationDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("CCP_ConnectionStrings__Platform")
            ?? DesignTimeConnectionString;

        DbContextOptions<IntegrationDbContext> options =
            new DbContextOptionsBuilder<IntegrationDbContext>()
                .UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable(
                        "__ef_migrations_history", IntegrationDbContext.SchemaName))
                .Options;

        return new IntegrationDbContext(options);
    }
}
