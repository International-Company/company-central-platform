using CCP.Kernel.Infrastructure.Outbox;
using CCP.Kernel.Infrastructure.Persistence;
using CCP.Modules.Integrations.Domain.Logging;
using CCP.Modules.Integrations.Domain.Providers;
using CCP.Modules.Integrations.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace CCP.Modules.Integrations.Infrastructure.Persistence;

/// <summary>
/// The <c>integrations</c> schema.
/// <para>
/// One DbContext and one migration history per module (ADR-004). <b>No column
/// here holds a credential</b> — the providers table holds the <i>name</i> of a
/// secret, and there is no column that could hold its value (§19.3).
/// </para>
/// </summary>
public sealed class IntegrationDbContext(DbContextOptions<IntegrationDbContext> options)
    : DbContext(options)
{
    public const string SchemaName = "integrations";

    public DbSet<IntegrationProvider> Providers => Set<IntegrationProvider>();

    public DbSet<IntegrationEndpoint> Endpoints => Set<IntegrationEndpoint>();

    public DbSet<IntegrationCallLog> CallLog => Set<IntegrationCallLog>();

    public DbSet<WebhookReceipt> WebhookReceipts => Set<WebhookReceipt>();

    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();

    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        ConfigureProviders(modelBuilder);
        ConfigureEndpoints(modelBuilder);
        ConfigureCallLog(modelBuilder);
        ConfigureWebhookReceipts(modelBuilder);
        ConfigureWebhookSubscriptions(modelBuilder);
        ConfigureWebhookDeliveries(modelBuilder);

        modelBuilder.ConfigureOutbox();

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplySnakeCaseNames();
    }

    private static void ConfigureProviders(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<IntegrationProvider>(entity =>
        {
            entity.ToTable("providers");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Code).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.BaseAddress).HasMaxLength(500).IsRequired();

            // Deliberately short. A reference is a name like
            // "integrations/acme/api-key"; a column wide enough to hold a
            // certificate is an invitation to paste one.
            entity.Property(e => e.CredentialReference).HasMaxLength(200);

            entity.Property(e => e.RedactedFields).HasMaxLength(2000);

            entity.HasIndex(e => e.Code).HasDatabaseName("ux_providers_code").IsUnique();

            entity.HasMany(e => e.Endpoints)
                .WithOne()
                .HasForeignKey(e => e.ProviderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Navigation(e => e.Endpoints).AutoInclude();

            entity.Ignore(e => e.DomainEvents);
            entity.Ignore(e => e.RedactionPolicy);
        });

    private static void ConfigureEndpoints(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<IntegrationEndpoint>(entity =>
        {
            entity.ToTable("endpoints");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Key).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Method).HasMaxLength(10).IsRequired();
            entity.Property(e => e.PathTemplate).HasMaxLength(500).IsRequired();

            entity.HasIndex(e => new { e.ProviderId, e.Key })
                .HasDatabaseName("ux_endpoints_provider_key")
                .IsUnique();
        });

    private static void ConfigureCallLog(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<IntegrationCallLog>(entity =>
        {
            entity.ToTable("call_log");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.ProviderCode).HasMaxLength(64).IsRequired();
            entity.Property(e => e.EndpointKey).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Method).HasMaxLength(10).IsRequired();
            entity.Property(e => e.Path).HasMaxLength(500).IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.FailureReason).HasMaxLength(1000);
            entity.Property(e => e.Outcome).HasConversion<int>();

            // The administration query: this provider, newest first.
            entity.HasIndex(e => new { e.ProviderCode, e.StartedAt })
                .HasDatabaseName("ix_call_log_provider");

            // "Which calls did this request cause?" — asked during every
            // incident that begins with a single failing request.
            entity.HasIndex(e => e.CorrelationId)
                .HasDatabaseName("ix_call_log_correlation");

            // The retention sweep, which runs on a timer and wants the oldest
            // rows. Also the only index that keeps the sweep's cost from growing
            // with the table it is there to bound.
            entity.HasIndex(e => e.StartedAt).HasDatabaseName("ix_call_log_started");

            // No foreign key to providers. The log outlives the provider row: a
            // provider removed next year must not take its own history with it,
            // which is why the code is denormalised onto every entry.
        });

    private static void ConfigureWebhookReceipts(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<WebhookReceipt>(entity =>
        {
            entity.ToTable("webhook_receipts");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.ProviderCode).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Signature).HasMaxLength(128).IsRequired();

            // Unique, and that uniqueness is the replay defence rather than a
            // tidiness constraint. Two concurrent deliveries of the same webhook
            // both pass the "have we seen this?" read; only one survives the
            // insert, and the database is what decides.
            entity.HasIndex(e => new { e.ProviderCode, e.Signature })
                .HasDatabaseName("ux_webhook_receipts_signature")
                .IsUnique();

            entity.HasIndex(e => e.ReceivedAt).HasDatabaseName("ix_webhook_receipts_received");
        });

    private static void ConfigureWebhookSubscriptions(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<WebhookSubscription>(entity =>
        {
            entity.ToTable("webhook_subscriptions");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Endpoint).HasMaxLength(500).IsRequired();

            // A reference, never a value. There is no column here a secret could
            // fit in by accident, which is what makes a leaked backup not a leak
            // of every subscriber's signing key.
            entity.Property(e => e.SecretReference).HasMaxLength(200).IsRequired();

            entity.Property(e => e.SuspendedReason).HasMaxLength(500);

            // A list of names in one column rather than a child table. They are
            // read together, written together and never queried individually --
            // a join table would buy nothing and cost a query on every event.
            entity.PrimitiveCollection(e => e.EventTypes)
                  .HasColumnName("event_types")
                  .HasField("_eventTypes")
                  .UsePropertyAccessMode(PropertyAccessMode.Field)
                  .IsRequired();

            // No foreign key to the application: it lives in another schema, and
            // no foreign key crosses a schema boundary (ADR-004). The id is
            // checked through Authorization's contract when the subscription is
            // registered.
            entity.HasIndex(e => e.ApplicationId)
                .HasDatabaseName("ix_webhook_subscriptions_application");

            entity.Ignore(e => e.DomainEvents);
        });

    private static void ConfigureWebhookDeliveries(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<WebhookDelivery>(entity =>
        {
            entity.ToTable("webhook_deliveries");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.EventType).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Payload).IsRequired();
            entity.Property(e => e.Status).HasConversion<int>().IsRequired();
            entity.Property(e => e.LastError).HasMaxLength(500);

            // The sweep's only query: what is due, oldest first. Without this it
            // is a scan of every delivery ever made, every few seconds, for ever.
            entity.HasIndex(e => new { e.Status, e.NextAttemptAt })
                .HasDatabaseName("ix_webhook_deliveries_due");

            // One delivery per subscription per event. At-least-once delivery
            // means the fan-out can run twice on the same event -- after a crash
            // between committing the outbox and committing the rows -- and this
            // is what stops the second run from sending everything again.
            entity.HasIndex(e => new { e.SubscriptionId, e.EventId })
                .HasDatabaseName("ux_webhook_deliveries_event")
                .IsUnique();

            entity.Ignore(e => e.DomainEvents);
        });
}
