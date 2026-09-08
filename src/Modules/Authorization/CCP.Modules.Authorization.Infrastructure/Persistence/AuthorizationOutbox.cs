using System.Text.Json;
using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Domain;
using CCP.Kernel.Infrastructure.Outbox;
using CCP.Modules.Authorization.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CCP.Modules.Authorization.Infrastructure.Persistence;

/// <summary>
/// Stages Authorization integration events on the module's own DbContext, so
/// they commit with the change that produced them (ARCHITECTURE.md §8.5).
/// </summary>
public sealed class AuthorizationOutbox(
    AuthorizationDbContext dbContext,
    IRequestContext requestContext) : IAuthorizationOutbox
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

/// <summary>Commits the Authorization module's changes.</summary>
public sealed class AuthorizationUnitOfWork(AuthorizationDbContext dbContext) : IAuthorizationUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => dbContext.SaveChangesAsync(cancellationToken);
}

/// <summary>Design-time factory, so migrations generate without a configured environment.</summary>
public sealed class AuthorizationDbContextFactory : IDesignTimeDbContextFactory<AuthorizationDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=ccp_design_time;Username=design;Password=design";

    public AuthorizationDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("CCP_ConnectionStrings__Platform")
            ?? DesignTimeConnectionString;

        DbContextOptions<AuthorizationDbContext> options =
            new DbContextOptionsBuilder<AuthorizationDbContext>()
                .UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable(
                        "__ef_migrations_history", AuthorizationDbContext.SchemaName))
                .Options;

        return new AuthorizationDbContext(options);
    }
}
