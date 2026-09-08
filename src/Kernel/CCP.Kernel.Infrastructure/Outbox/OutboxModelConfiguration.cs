using CCP.Kernel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CCP.Kernel.Infrastructure.Outbox;

/// <summary>
/// Maps the outbox table into a module's own <see cref="DbContext"/>.
/// <para>
/// <b>Why every module maps the same table.</b> The outbox only works if an
/// event is committed in the <i>same transaction</i> as the change it describes
/// (ARCHITECTURE.md §8.5). If a module saved its entities through its own
/// context while the outbox row went through a different one, those would be two
/// transactions: a crash between them would either lose an event for a change
/// that happened, or record an event for a change that rolled back. Both defeat
/// the entire point.
/// </para>
/// <para>
/// Mapping <c>kernel.outbox_messages</c> into each module context means one
/// <c>SaveChanges</c> commits the entities and their events together, with no
/// shared-connection coordination and no distributed transaction. The relay
/// reads the same physical table through <see cref="KernelDbContext"/>, which is
/// a separate read path and needs no coordination with the writers.
/// </para>
/// </summary>
public static class OutboxModelConfiguration
{
    /// <summary>
    /// Adds the outbox mapping to a module's model. Call from
    /// <c>OnModelCreating</c>, before the naming convention is applied.
    /// </summary>
    public static ModelBuilder ConfigureOutbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            // Explicitly in the kernel schema, whatever the module's default is.
            entity.ToTable("outbox_messages", KernelDbContext.SchemaName);
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.EventType).HasMaxLength(200).IsRequired();
            entity.Property(e => e.PayloadType).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(128);
            entity.Property(e => e.LastError).HasMaxLength(4000);

            // The table and its indexes are created by the kernel migration.
            // A module maps it to write into it; it does not own its schema, and
            // must not generate a second CREATE TABLE for it.
            entity.ToTable(t => t.ExcludeFromMigrations());
        });

        return modelBuilder;
    }
}
