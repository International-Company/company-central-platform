using System.Text.Json;
using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Domain;
using CCP.Kernel.Infrastructure.Outbox;
using CCP.Modules.Organization.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CCP.Modules.Organization.Infrastructure.Persistence;

/// <summary>
/// Stages Organization integration events on the module's own DbContext, so a
/// staged event commits in the same transaction as the change that produced it
/// (ARCHITECTURE.md §8.5).
/// </summary>
public sealed class OrganizationOutbox(
    OrganizationDbContext dbContext,
    IRequestContext requestContext) : IOrganizationOutbox
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
/// Commits the Organization module's changes.
/// <para>
/// One <c>SaveChanges</c> on one context. This is what makes a unit move atomic:
/// the moved unit and every rebased descendant land together, or not at all.
/// </para>
/// </summary>
public sealed class OrganizationUnitOfWork(OrganizationDbContext dbContext) : IOrganizationUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => dbContext.SaveChangesAsync(cancellationToken);
}

/// <summary>
/// Builds an <see cref="OrganizationDbContext"/> for design-time tooling, so
/// migrations can be generated without a configured environment.
/// </summary>
public sealed class OrganizationDbContextFactory : IDesignTimeDbContextFactory<OrganizationDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=ccp_design_time;Username=design;Password=design";

    public OrganizationDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("CCP_ConnectionStrings__Platform")
            ?? DesignTimeConnectionString;

        DbContextOptions<OrganizationDbContext> options =
            new DbContextOptionsBuilder<OrganizationDbContext>()
                .UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable(
                        "__ef_migrations_history", OrganizationDbContext.SchemaName))
                .Options;

        return new OrganizationDbContext(options);
    }
}
