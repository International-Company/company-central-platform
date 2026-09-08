using CCP.Kernel.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace CCP.Kernel.Infrastructure.Persistence;

/// <summary>
/// The <c>kernel</c> schema: cross-cutting infrastructure tables that belong to
/// no module. At present that is the outbox alone.
/// <para>
/// Each module owns its own DbContext and its own schema, with its own
/// migration history, so modules version independently (ADR-004).
/// </para>
/// </summary>
public sealed class KernelDbContext(DbContextOptions<KernelDbContext> options) : DbContext(options)
{
    public const string SchemaName = "kernel";

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.EventType).HasMaxLength(200).IsRequired();
            entity.Property(e => e.PayloadType).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(128);
            entity.Property(e => e.LastError).HasMaxLength(4000);

            // The relay's only query: unprocessed, not dead-lettered, and due.
            // A partial index keeps this small even when the table holds
            // millions of processed rows.
            entity.HasIndex(e => new { e.NextAttemptAt })
                  .HasDatabaseName("ix_outbox_messages_pending")
                  .HasFilter("processed_at IS NULL AND dead_lettered_at IS NULL");

            // Supports the cleanup job that removes old processed rows.
            entity.HasIndex(e => e.ProcessedAt)
                  .HasDatabaseName("ix_outbox_messages_processed_at")
                  .HasFilter("processed_at IS NOT NULL");
        });

        base.OnModelCreating(modelBuilder);

        // Applied last, so it rewrites every name configured above. Without
        // this the columns are PascalCase and the partial index filters, which
        // are written in snake_case, refer to columns that do not exist.
        modelBuilder.ApplySnakeCaseNames();
    }
}
