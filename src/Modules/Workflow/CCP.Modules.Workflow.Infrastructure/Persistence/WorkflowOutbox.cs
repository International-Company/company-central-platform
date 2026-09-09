using System.Text.Json;
using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Domain;
using CCP.Kernel.Infrastructure.Outbox;
using CCP.Modules.Workflow.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CCP.Modules.Workflow.Infrastructure.Persistence;

/// <summary>
/// Stages Workflow integration events on the module's own DbContext, so a
/// staged event commits in the same transaction as the change that produced it
/// (ARCHITECTURE.md §8.5).
/// <para>
/// It matters more here than elsewhere: a completion event sent before the
/// commit tells a business system to release a purchase order that was never
/// approved, and one lost after the commit leaves an approval nobody acts on.
/// </para>
/// </summary>
public sealed class WorkflowOutbox(
    WorkflowDbContext dbContext,
    IRequestContext requestContext) : IWorkflowOutbox
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
/// Commits the Workflow module's changes.
/// <para>
/// One <c>SaveChanges</c> on one context, which is what makes an action atomic:
/// the recorded action, the moved instance, the settled task, the withdrawn
/// siblings and the staged event all land together or not at all. Half of that
/// would be an approval that happened with no task closed, or a task closed
/// with no approval.
/// </para>
/// </summary>
public sealed class WorkflowUnitOfWork(WorkflowDbContext dbContext) : IWorkflowUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => dbContext.SaveChangesAsync(cancellationToken);
}

/// <summary>
/// Builds a <see cref="WorkflowDbContext"/> for design-time tooling, so
/// migrations can be generated without a configured environment.
/// </summary>
public sealed class WorkflowDbContextFactory : IDesignTimeDbContextFactory<WorkflowDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=ccp_design_time;Username=design;Password=design";

    public WorkflowDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("CCP_ConnectionStrings__Platform")
            ?? DesignTimeConnectionString;

        DbContextOptions<WorkflowDbContext> options =
            new DbContextOptionsBuilder<WorkflowDbContext>()
                .UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable(
                        "__ef_migrations_history", WorkflowDbContext.SchemaName))
                .Options;

        return new WorkflowDbContext(options);
    }
}
