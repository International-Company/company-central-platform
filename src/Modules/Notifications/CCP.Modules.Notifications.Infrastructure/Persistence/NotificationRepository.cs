using System.Text.Json;
using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Domain;
using CCP.Kernel.Infrastructure.Outbox;
using CCP.Modules.Notifications.Application.Abstractions;
using CCP.Modules.Notifications.Domain.Notifications;
using CCP.Modules.Notifications.Domain.Preferences;
using CCP.Modules.Notifications.Domain.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CCP.Modules.Notifications.Infrastructure.Persistence;

/// <summary>Reads and writes the notifications schema.</summary>
public sealed class NotificationRepository(NotificationDbContext dbContext) : INotificationRepository
{
    // --- Templates ----------------------------------------------------------

    public async Task<NotificationTemplate?> FindTemplateAsync(
        string code, string locale, CancellationToken cancellationToken = default)
    {
        string normalised = code.Trim().ToLowerInvariant();

        return await dbContext.Templates
            .FirstOrDefaultAsync(
                t => t.Code == normalised && t.Locale == locale, cancellationToken);
    }

    public async Task<NotificationTemplate?> FindTemplateByIdAsync(
        Guid templateId, CancellationToken cancellationToken = default)
        => await dbContext.Templates
            .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);

    public async Task<IReadOnlyList<NotificationTemplate>> GetTemplatesAsync(
        bool includeInactive, CancellationToken cancellationToken = default)
    {
        IQueryable<NotificationTemplate> query = dbContext.Templates.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(t => t.IsActive);
        }

        return await query
            .OrderBy(t => t.Code)
            .ThenBy(t => t.Locale)
            .ToListAsync(cancellationToken);
    }

    public void AddTemplate(NotificationTemplate template)
        => dbContext.Templates.Add(template);

    // --- Notifications ------------------------------------------------------

    public async Task<Notification?> FindNotificationAsync(
        Guid notificationId, CancellationToken cancellationToken = default)
        => await dbContext.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId, cancellationToken);

    public async Task<(IReadOnlyList<Notification> Items, long Total)> GetInboxAsync(
        Guid userId, bool unreadOnly, int skip, int take,
        CancellationToken cancellationToken = default)
    {
        // In-app only. An email that was sent is not an item in an inbox, and
        // showing one would mean the same message appearing twice for anybody
        // who receives both.
        IQueryable<Notification> query = dbContext.Notifications
            .AsNoTracking()
            .Where(n => n.RecipientUserId == userId && n.Channel == NotificationChannel.InApp);

        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        long total = await query.LongCountAsync(cancellationToken);

        List<Notification> items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<(IReadOnlyList<Notification> Items, long Total)> SearchAsync(
        NotificationStatus? status, string? category, NotificationChannel? channel,
        int skip, int take, CancellationToken cancellationToken = default)
    {
        IQueryable<Notification> query = dbContext.Notifications.AsNoTracking();

        if (status is { } wanted)
        {
            query = query.Where(n => n.Status == wanted);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            string normalised = category.Trim().ToLowerInvariant();

            query = query.Where(n => n.Category == normalised);
        }

        if (channel is { } wantedChannel)
        {
            query = query.Where(n => n.Channel == wantedChannel);
        }

        long total = await query.LongCountAsync(cancellationToken);

        List<Notification> items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<IReadOnlyList<Notification>> GetPendingAsync(
        int limit, CancellationToken cancellationToken = default)
        => await dbContext.Notifications
            .Where(n => n.Status == NotificationStatus.Pending)
            .OrderBy(n => n.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task<bool> HasProcessedAsync(
        Guid eventId, string reason, CancellationToken cancellationToken = default)
        => dbContext.ProcessedEvents
            .AsNoTracking()
            .AnyAsync(e => e.EventId == eventId && e.Reason == reason, cancellationToken);

    public void MarkProcessed(ProcessedEvent processed) =>
        dbContext.ProcessedEvents.Add(processed);

    public void AddNotification(Notification notification)
        => dbContext.Notifications.Add(notification);

    // --- Preferences --------------------------------------------------------

    public async Task<IReadOnlyList<NotificationPreference>> GetPreferencesAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => await dbContext.Preferences
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);

    public async Task<NotificationPreference?> FindPreferenceAsync(
        Guid userId, string category, NotificationChannel channel,
        CancellationToken cancellationToken = default)
    {
        string normalised = category.Trim().ToLowerInvariant();

        return await dbContext.Preferences.FirstOrDefaultAsync(
            p => p.UserId == userId && p.Category == normalised && p.Channel == channel,
            cancellationToken);
    }

    public void AddPreference(NotificationPreference preference)
        => dbContext.Preferences.Add(preference);
}

/// <summary>Stages Notifications integration events on the module's own context.</summary>
public sealed class NotificationOutbox(
    NotificationDbContext dbContext,
    IRequestContext requestContext) : INotificationOutbox
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

/// <summary>Commits the Notifications module's changes.</summary>
public sealed class NotificationUnitOfWork(NotificationDbContext dbContext) : INotificationUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => dbContext.SaveChangesAsync(cancellationToken);
}

/// <summary>Builds the context for design-time tooling.</summary>
public sealed class NotificationDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=ccp_design_time;Username=design;Password=design";

    public NotificationDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("CCP_ConnectionStrings__Platform")
            ?? DesignTimeConnectionString;

        DbContextOptions<NotificationDbContext> options =
            new DbContextOptionsBuilder<NotificationDbContext>()
                .UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable(
                        "__ef_migrations_history", NotificationDbContext.SchemaName))
                .Options;

        return new NotificationDbContext(options);
    }
}
