using CCP.Kernel.Infrastructure.Persistence;
using CCP.Modules.Configuration.Domain.Flags;
using CCP.Modules.Configuration.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace CCP.Modules.Configuration.Infrastructure.Persistence;

/// <summary>
/// The <c>configuration</c> schema.
/// <para>
/// One DbContext and one migration history per module (ADR-004). No outbox here:
/// this module raises no integration events, and mapping one in would create a
/// table nothing writes to.
/// </para>
/// </summary>
public sealed class ConfigurationDbContext(DbContextOptions<ConfigurationDbContext> options)
    : DbContext(options)
{
    public const string SchemaName = "configuration";

    public DbSet<SettingDefinition> Definitions => Set<SettingDefinition>();

    public DbSet<SettingValue> Values => Set<SettingValue>();

    public DbSet<SettingChange> Changes => Set<SettingChange>();

    public DbSet<FeatureFlag> Flags => Set<FeatureFlag>();

    public DbSet<ConfigurationVersionRow> Version => Set<ConfigurationVersionRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<SettingDefinition>(entity =>
        {
            entity.ToTable("definitions");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Key).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ApplicationCode).HasMaxLength(32).IsRequired();
            entity.Property(e => e.ValueType).HasConversion<int>();
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.DefaultValue).HasMaxLength(4000);
            entity.Property(e => e.AllowedValues).HasMaxLength(4000);

            entity.HasIndex(e => e.Key).HasDatabaseName("ux_definitions_key").IsUnique();

            entity.HasIndex(e => e.ApplicationCode)
                .HasDatabaseName("ix_definitions_application");

            entity.Ignore(e => e.DomainEvents);
            entity.Ignore(e => e.Choices);
        });

        modelBuilder.Entity<SettingValue>(entity =>
        {
            entity.ToTable("values");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Key).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Scope).HasConversion<int>();
            entity.Property(e => e.Value).HasMaxLength(4000).IsRequired();

            // One value per key per scope. Two would make resolution depend on
            // which row the planner happened to read first, which is the kind of
            // bug that reproduces on one instance and not another.
            entity.HasIndex(e => new { e.Key, e.Scope, e.ScopeId })
                .HasDatabaseName("ux_values_key_scope")
                .IsUnique();

            entity.Ignore(e => e.DomainEvents);
        });

        modelBuilder.Entity<SettingChange>(entity =>
        {
            entity.ToTable("changes");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Key).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Scope).HasConversion<int>();
            entity.Property(e => e.OldValue).HasMaxLength(4000);
            entity.Property(e => e.NewValue).HasMaxLength(4000);
            entity.Property(e => e.Reason).HasMaxLength(1000);

            entity.HasIndex(e => new { e.Key, e.ChangedAt })
                .HasDatabaseName("ix_changes_key");

            // "What has this person been changing?" — asked when a behaviour
            // moved and the trail is the only account of it.
            entity.HasIndex(e => new { e.ChangedBy, e.ChangedAt })
                .HasDatabaseName("ix_changes_actor");
        });

        modelBuilder.Entity<FeatureFlag>(entity =>
        {
            entity.ToTable("feature_flags");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Key).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ApplicationCode).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.TargetedRoles).HasMaxLength(4000);
            entity.Property(e => e.TargetedUnits).HasMaxLength(4000);

            entity.HasIndex(e => e.Key).HasDatabaseName("ux_feature_flags_key").IsUnique();

            entity.Ignore(e => e.DomainEvents);
            entity.Ignore(e => e.RoleTargets);
            entity.Ignore(e => e.UnitTargets);
            entity.Ignore(e => e.IsUntargeted);
        });

        modelBuilder.Entity<ConfigurationVersionRow>(entity =>
        {
            entity.ToTable("version");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();

            entity.HasData(new ConfigurationVersionRow
            {
                Id = 1,
                Version = 1,
                UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
            });
        });

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplySnakeCaseNames();
    }
}

/// <summary>
/// The single row every instance checks before trusting its cached
/// configuration.
/// <para>
/// In the database rather than in memory, so an instance that did not make a
/// change still notices it. Without that, a Platform running three instances
/// would apply a setting change on one of them and nobody would know which.
/// </para>
/// </summary>
public sealed class ConfigurationVersionRow
{
    public int Id { get; set; }

    public long Version { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
