using CCP.Kernel.Infrastructure.Persistence;
using CCP.Modules.Audit.Domain;
using Microsoft.EntityFrameworkCore;

namespace CCP.Modules.Audit.Infrastructure.Persistence;

/// <summary>
/// The <c>audit</c> schema.
/// <para>
/// <b>No foreign key leaves this schema, and none points at users either.</b>
/// Beyond the schema-boundary rule (ADR-004 §10.2), an audit row must outlive
/// what it describes: a deleted user's actions stay on the record, and a
/// required relationship would drop precisely the rows an investigation needs.
/// The actor's username is stored alongside the id for the same reason.
/// </para>
/// <para>
/// The outbox is <b>not</b> mapped here. Audit is a terminal sink — it consumes
/// events and raises none — so a module outbox would be an empty table inviting
/// someone to fill it.
/// </para>
/// </summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public const string SchemaName = "audit";

    public const string EventsTable = "audit_events";

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable(EventsTable);

            // The primary key includes occurred_at because PostgreSQL requires
            // the partition key to be part of every unique constraint on a
            // partitioned table. Id alone would be rejected outright.
            entity.HasKey(e => new { e.Id, e.OccurredAt });

            entity.Property(e => e.Id).ValueGeneratedNever();

            entity.Property(e => e.Application).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Module).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Action).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Result).HasConversion<int>().IsRequired();

            entity.Property(e => e.ResourceType).HasMaxLength(128);
            entity.Property(e => e.ResourceId).HasMaxLength(128);
            entity.Property(e => e.ActorUsername).HasMaxLength(64);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasMaxLength(512);
            entity.Property(e => e.DeviceId).HasMaxLength(128);
            entity.Property(e => e.RequestId).HasMaxLength(128);
            entity.Property(e => e.CorrelationId).HasMaxLength(128);

            entity.Property(e => e.OldValue).HasColumnType("jsonb");
            entity.Property(e => e.NewValue).HasColumnType("jsonb");
            entity.Property(e => e.Metadata).HasColumnType("jsonb");

            // One index per question actually asked (ARCHITECTURE.md §15.5),
            // each leading with the filter and ending with time so the range
            // bound is served by the same index rather than by a second pass.
            //
            // Every search is already narrowed to a few partitions by the
            // mandatory date range; these decide what happens inside them.
            entity.HasIndex(e => new { e.ActorUserId, e.OccurredAt })
                  .HasDatabaseName("ix_audit_actor_time");

            entity.HasIndex(e => new { e.Application, e.Module, e.OccurredAt })
                  .HasDatabaseName("ix_audit_application_module_time");

            entity.HasIndex(e => new { e.Action, e.OccurredAt })
                  .HasDatabaseName("ix_audit_action_time");

            entity.HasIndex(e => new { e.ResourceType, e.ResourceId, e.OccurredAt })
                  .HasDatabaseName("ix_audit_resource_time");

            entity.HasIndex(e => new { e.Result, e.OccurredAt })
                  .HasDatabaseName("ix_audit_result_time");

            entity.HasIndex(e => e.CorrelationId)
                  .HasDatabaseName("ix_audit_correlation");
        });

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplySnakeCaseNames();
    }
}
