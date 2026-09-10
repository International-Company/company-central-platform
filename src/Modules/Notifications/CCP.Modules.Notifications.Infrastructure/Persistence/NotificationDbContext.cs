using CCP.Kernel.Infrastructure.Outbox;
using CCP.Kernel.Infrastructure.Persistence;
using CCP.Modules.Notifications.Domain.Notifications;
using CCP.Modules.Notifications.Domain.Preferences;
using CCP.Modules.Notifications.Domain.Templates;
using Microsoft.EntityFrameworkCore;

namespace CCP.Modules.Notifications.Infrastructure.Persistence;

/// <summary>
/// The <c>notifications</c> schema.
/// <para>
/// One DbContext and one migration history per module (ADR-004). <b>No foreign
/// key leaves this schema</b> — <c>RecipientUserId</c> and <c>UserId</c> point
/// at <c>identity.users</c> and are deliberately just columns.
/// </para>
/// </summary>
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options)
    : DbContext(options)
{
    public const string SchemaName = "notifications";

    public DbSet<NotificationTemplate> Templates => Set<NotificationTemplate>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<NotificationDelivery> Deliveries => Set<NotificationDelivery>();

    public DbSet<NotificationPreference> Preferences => Set<NotificationPreference>();

    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        ConfigureTemplates(modelBuilder);
        ConfigureNotifications(modelBuilder);
        ConfigureDeliveries(modelBuilder);
        ConfigurePreferences(modelBuilder);
        ConfigureProcessedEvents(modelBuilder);

        modelBuilder.ConfigureOutbox();

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplySnakeCaseNames();
    }

    private static void ConfigureTemplates(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<NotificationTemplate>(entity =>
        {
            entity.ToTable("templates");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Code).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Locale).HasMaxLength(8).IsRequired();
            entity.Property(e => e.Subject).HasMaxLength(300);
            entity.Property(e => e.Body).IsRequired();
            entity.Property(e => e.Variables).HasMaxLength(1000);

            // One template per message per language. Two rows for the same code
            // and locale would make "which text did they get?" unanswerable.
            entity.HasIndex(e => new { e.Code, e.Locale })
                .HasDatabaseName("ux_templates_code_locale")
                .IsUnique();

            entity.Ignore(e => e.DomainEvents);
            entity.Ignore(e => e.DeclaredVariables);
        });

    private static void ConfigureNotifications(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Category).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Channel).HasConversion<int>();
            entity.Property(e => e.Locale).HasMaxLength(8).IsRequired();
            entity.Property(e => e.Subject).HasMaxLength(300);
            entity.Property(e => e.Body).IsRequired();
            entity.Property(e => e.TemplateCode).HasMaxLength(100);
            entity.Property(e => e.Status).HasConversion<int>();

            // The inbox query: this person's, newest first. The one people wait
            // on, so it is the one that gets an index.
            entity.HasIndex(e => new { e.RecipientUserId, e.Channel, e.CreatedAt })
                .HasDatabaseName("ix_notifications_recipient");

            // The dispatcher's sweep. Partial, because it runs every fifteen
            // seconds and only ever wants what is still pending — scanning every
            // notification ever sent to find them would make the sweep cost grow
            // with history, which for this table is fast.
            entity.HasIndex(e => new { e.Status, e.CreatedAt })
                .HasDatabaseName("ix_notifications_pending")
                .HasFilter("status = 1");

            entity.HasMany(e => e.Deliveries)
                .WithOne()
                .HasForeignKey(d => d.NotificationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Navigation(e => e.Deliveries).AutoInclude();

            entity.Ignore(e => e.DomainEvents);
        });

    private static void ConfigureDeliveries(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<NotificationDelivery>(entity =>
        {
            entity.ToTable("deliveries");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.ProviderName).HasMaxLength(64);
            entity.Property(e => e.ProviderResponse).HasMaxLength(1000);

            entity.HasIndex(e => new { e.NotificationId, e.Attempt })
                .HasDatabaseName("ux_deliveries_notification_attempt")
                .IsUnique();
        });

    private static void ConfigurePreferences(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<NotificationPreference>(entity =>
        {
            entity.ToTable("preferences");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Category).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Channel).HasConversion<int>();

            entity.HasIndex(e => new { e.UserId, e.Category, e.Channel })
                .HasDatabaseName("ux_preferences_user_category_channel")
                .IsUnique();

            entity.Ignore(e => e.DomainEvents);
        });

    /// <summary>
    /// The note that an event has already produced its notification.
    /// <para>
    /// Outbox delivery is at-least-once by design, so a listener can be handed
    /// the same event twice. This is how the second time is recognised.
    /// </para>
    /// </summary>
    private static void ConfigureProcessedEvents(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.ToTable("processed_events");

            // The event and the reason together. Two listeners may legitimately
            // react to one event, and a key on the event alone would let
            // whichever ran first silence the other for ever.
            entity.HasKey(e => new { e.EventId, e.Reason });

            entity.Property(e => e.Reason).HasMaxLength(100).IsRequired();

            // Supports the retention sweep, which is the only query that reads
            // across every row rather than looking one up.
            entity.HasIndex(e => e.ProcessedAt)
                  .HasDatabaseName("ix_processed_events_processed_at");
        });
}
