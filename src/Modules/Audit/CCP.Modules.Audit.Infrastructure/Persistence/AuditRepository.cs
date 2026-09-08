using System.Globalization;
using CCP.Modules.Audit.Application.Abstractions;
using CCP.Modules.Audit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CCP.Modules.Audit.Infrastructure.Persistence;

/// <summary>EF Core implementation of the trail.</summary>
public sealed class AuditRepository(IDbContextFactory<AuditDbContext> contextFactory) : IAuditRepository
{
    /// <summary>
    /// Appends and commits, on a context of its own.
    /// <para>
    /// Its own context and its own transaction, deliberately. An audit write
    /// must neither fail the operation it records nor be rolled back with it: a
    /// denied action rolls back everything it touched, and the record of that
    /// denial is exactly what has to survive.
    /// </para>
    /// </summary>
    public async Task<int> AppendAsync(
        IReadOnlyList<AuditEvent> events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count == 0)
        {
            return 0;
        }

        await using AuditDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        context.AuditEvents.AddRange(events);

        return await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<AuditEvent> Items, long TotalCount)> SearchAsync(
        AuditSearchCriteria criteria,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        await using AuditDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // The date range comes first and is never optional. It is what lets
        // PostgreSQL prune partitions, which is the difference between reading
        // three months and reading everything ever recorded.
        IQueryable<AuditEvent> query = context.AuditEvents
            .AsNoTracking()
            .Where(e => e.OccurredAt >= criteria.From && e.OccurredAt <= criteria.To);

        if (criteria.Application is { } application)
        {
            query = query.Where(e => e.Application == application);
        }

        if (criteria.Module is { } module)
        {
            query = query.Where(e => e.Module == module);
        }

        if (criteria.Action is { } action)
        {
            query = query.Where(e => e.Action == action);
        }

        if (criteria.ActorUserId is { } actorUserId)
        {
            query = query.Where(e => e.ActorUserId == actorUserId);
        }

        if (criteria.ResourceType is { } resourceType)
        {
            query = query.Where(e => e.ResourceType == resourceType);
        }

        if (criteria.ResourceId is { } resourceId)
        {
            query = query.Where(e => e.ResourceId == resourceId);
        }

        if (criteria.Result is { } result)
        {
            query = query.Where(e => e.Result == result);
        }

        long total = await query.LongCountAsync(cancellationToken);

        List<AuditEvent> items = await query
            .OrderByDescending(e => e.OccurredAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    /// <summary>
    /// Creates any missing monthly partitions covering the window.
    /// <para>
    /// <c>IF NOT EXISTS</c> throughout, so this is safe to run on every startup
    /// and safe to run from several instances at once.
    /// </para>
    /// </summary>
    public async Task EnsurePartitionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        await using AuditDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var month = new DateTimeOffset(from.Year, from.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var last = new DateTimeOffset(to.Year, to.Month, 1, 0, 0, 0, TimeSpan.Zero);

        while (month <= last)
        {
            DateTimeOffset next = month.AddMonths(1);

            string suffix = month.ToString("yyyy_MM", CultureInfo.InvariantCulture);
            string lower = month.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string upper = next.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            string sql =
                $"""
                 CREATE TABLE IF NOT EXISTS {AuditDbContext.SchemaName}.{AuditDbContext.EventsTable}_{suffix}
                 PARTITION OF {AuditDbContext.SchemaName}.{AuditDbContext.EventsTable}
                 FOR VALUES FROM ('{lower}') TO ('{upper}');
                 """;

            // The analyser is right in general and wrong here, so it is
            // suppressed at this one call rather than in .editorconfig.
            //
            // PostgreSQL does not accept parameters in DDL: a partition bound
            // cannot be bound. Every value in this statement is formatted from a
            // DateTimeOffset computed a few lines above, with an invariant
            // culture and a fixed format - none of it reaches here from a
            // caller, a request or the database. There is no untrusted text to
            // inject.
#pragma warning disable EF1002
            await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
#pragma warning restore EF1002

            month = next;
        }
    }
}

/// <summary>Design-time factory, so migrations generate without a configured environment.</summary>
public sealed class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=ccp_design_time;Username=design;Password=design";

    public AuditDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("CCP_ConnectionStrings__Platform")
            ?? DesignTimeConnectionString;

        DbContextOptions<AuditDbContext> options =
            new DbContextOptionsBuilder<AuditDbContext>()
                .UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__ef_migrations_history", AuditDbContext.SchemaName))
                .Options;

        return new AuditDbContext(options);
    }
}
