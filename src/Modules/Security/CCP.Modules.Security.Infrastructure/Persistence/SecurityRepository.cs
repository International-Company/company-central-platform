using System.Text.Json;
using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Domain;
using CCP.Kernel.Infrastructure.Outbox;
using CCP.Modules.Security.Application.Abstractions;
using CCP.Modules.Security.Domain.Events;
using CCP.Modules.Security.Domain.Mfa;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CCP.Modules.Security.Infrastructure.Persistence;

/// <summary>EF Core implementation of the Security module's persistence port.</summary>
public sealed class SecurityRepository(SecurityDbContext dbContext) : ISecurityRepository
{
    /// <summary>
    /// Loads an enrolment with its recovery codes. They are always needed
    /// together — verification may fall back to a recovery code, and disabling
    /// must invalidate them all.
    /// </summary>
    public Task<MfaEnrolment?> FindEnrolmentAsync(Guid userId, CancellationToken cancellationToken = default)
        => dbContext.MfaEnrolments
            .Include(e => e.RecoveryCodes)
            .FirstOrDefaultAsync(e => e.UserId == userId, cancellationToken);

    public Task<MfaEnrolment?> FindActiveEnrolmentAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => dbContext.MfaEnrolments
            .Include(e => e.RecoveryCodes)
            .FirstOrDefaultAsync(
                e => e.UserId == userId && e.Status == MfaEnrolmentStatus.Active, cancellationToken);

    public void AddEnrolment(MfaEnrolment enrolment) => dbContext.MfaEnrolments.Add(enrolment);

    public void RemoveEnrolment(MfaEnrolment enrolment) => dbContext.MfaEnrolments.Remove(enrolment);

    public Task AddSecurityEventAsync(
        SecurityEvent securityEvent, CancellationToken cancellationToken = default)
    {
        dbContext.SecurityEvents.Add(securityEvent);

        return Task.CompletedTask;
    }

    public void AddStepUpConfirmation(StepUpConfirmation confirmation)
        => dbContext.StepUpConfirmations.Add(confirmation);

    /// <summary>
    /// Whether this session holds a live elevation.
    /// <para>
    /// Both the user and the session must match. Checking the session alone
    /// would be enough in practice, but a mismatch between the two means
    /// something is wrong with the token, and the safe reading of "wrong" is
    /// "not elevated".
    /// </para>
    /// </summary>
    public Task<bool> HasValidStepUpAsync(
        Guid userId, Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken = default)
        => dbContext.StepUpConfirmations
            .AsNoTracking()
            .AnyAsync(
                c => c.SessionId == sessionId
                  && c.UserId == userId
                  && c.RevokedAt == null
                  && c.ExpiresAt > now,
                cancellationToken);

    /// <summary>
    /// Revokes every live elevation for a user.
    /// <para>
    /// ExecuteUpdate rather than load-then-save: this runs on a security-relevant
    /// change and must not depend on how many rows happen to be outstanding. It
    /// writes directly and does not need the unit of work.
    /// </para>
    /// </summary>
    public Task<int> RevokeStepUpConfirmationsAsync(
        Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default)
        => dbContext.StepUpConfirmations
            .Where(c => c.UserId == userId && c.RevokedAt == null && c.ExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(c => c.RevokedAt, now),
                cancellationToken);

    /// <summary>
    /// Searches security events.
    /// <para>
    /// The date range is <b>required</b>, not optional. An unbounded query over
    /// a table that grows with every failed sign-in is a scan, and on a busy day
    /// that is an outage caused by someone looking at a dashboard.
    /// </para>
    /// </summary>
    public async Task<(IReadOnlyList<SecurityEvent> Items, long TotalCount)> SearchSecurityEventsAsync(
        Guid? userId,
        string? eventType,
        SecuritySeverity? minimumSeverity,
        DateTimeOffset from,
        DateTimeOffset to,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        IQueryable<SecurityEvent> query = dbContext.SecurityEvents
            .AsNoTracking()
            .Where(e => e.OccurredAt >= from && e.OccurredAt <= to);

        if (userId is { } requiredUserId)
        {
            query = query.Where(e => e.UserId == requiredUserId);
        }

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query = query.Where(e => e.EventType == eventType);
        }

        if (minimumSeverity is { } severity)
        {
            query = query.Where(e => e.Severity >= severity);
        }

        long total = await query.LongCountAsync(cancellationToken);

        List<SecurityEvent> items = await query
            .OrderByDescending(e => e.OccurredAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (items, total);
    }
}

/// <summary>
/// Writes security events.
/// <para>
/// <b>Its own DbContext, committed independently.</b> A failed sign-in rolls back
/// everything else it touched, and the event recording that failure must survive
/// that rollback — otherwise the only attempts recorded would be the successful
/// ones, which is precisely backwards for detection.
/// </para>
/// <para>
/// Hence the factory rather than the ambient scoped context: sharing the
/// request's context would make <c>SaveChangesAsync</c> here commit whatever
/// else that context happened to be holding, which is both the opposite of
/// independent and a way to commit a half-finished operation.
/// </para>
/// </summary>
public sealed class SecurityEventRecorder(
    IDbContextFactory<SecurityDbContext> contextFactory) : ISecurityEventRecorder
{
    public async Task RecordAsync(
        SecurityEvent securityEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(securityEvent);

        await using SecurityDbContext dbContext =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        dbContext.SecurityEvents.Add(securityEvent);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Stages Security integration events on the module's own DbContext.</summary>
public sealed class SecurityOutbox(
    SecurityDbContext dbContext,
    IRequestContext requestContext) : ISecurityOutbox
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

/// <summary>Commits the Security module's changes.</summary>
public sealed class SecurityUnitOfWork(SecurityDbContext dbContext) : ISecurityUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => dbContext.SaveChangesAsync(cancellationToken);
}

/// <summary>Design-time factory, so migrations generate without a configured environment.</summary>
public sealed class SecurityDbContextFactory : IDesignTimeDbContextFactory<SecurityDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=ccp_design_time;Username=design;Password=design";

    public SecurityDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("CCP_ConnectionStrings__Platform")
            ?? DesignTimeConnectionString;

        DbContextOptions<SecurityDbContext> options =
            new DbContextOptionsBuilder<SecurityDbContext>()
                .UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable(
                        "__ef_migrations_history", SecurityDbContext.SchemaName))
                .Options;

        return new SecurityDbContext(options);
    }
}
