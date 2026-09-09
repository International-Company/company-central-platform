using System.Text.Json;
using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Domain;
using CCP.Kernel.Infrastructure.Outbox;
using CCP.Modules.Documents.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CCP.Modules.Documents.Infrastructure.Persistence;

/// <summary>Commits the Documents module's changes.</summary>
public sealed class DocumentUnitOfWork(DocumentDbContext dbContext) : IDocumentUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => dbContext.SaveChangesAsync(cancellationToken);
}

/// <summary>
/// The module's slice of the transactional outbox.
/// <para>
/// Mapped into this DbContext, so an event is written in the same transaction as
/// the change it describes: never recorded for something that rolled back, never
/// lost after something that committed (§8.5).
/// </para>
/// </summary>
public sealed class DocumentOutbox(DocumentDbContext dbContext, IRequestContext requestContext)
    : IDocumentOutbox
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
public sealed class DocumentDbContextFactory : IDesignTimeDbContextFactory<DocumentDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=ccp_design_time;Username=design;Password=design";

    public DocumentDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("CCP_ConnectionStrings__Platform")
            ?? DesignTimeConnectionString;

        DbContextOptions<DocumentDbContext> options =
            new DbContextOptionsBuilder<DocumentDbContext>()
                .UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable(
                        "__ef_migrations_history", DocumentDbContext.SchemaName))
                .Options;

        return new DocumentDbContext(options);
    }
}
