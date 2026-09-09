using CCP.Kernel.Infrastructure.Outbox;
using CCP.Kernel.Infrastructure.Persistence;
using CCP.Modules.Authorization.Domain.Applications;
using CCP.Modules.Authorization.Domain.Permissions;
using CCP.Modules.Authorization.Domain.Roles;
using Microsoft.EntityFrameworkCore;

namespace CCP.Modules.Authorization.Infrastructure.Persistence;

/// <summary>
/// The <c>authz</c> schema.
/// <para>
/// Small, extremely hot, and read on every request. <b>No foreign key leaves
/// this schema</b> — user ids point at <c>identity.users</c> and scope unit ids
/// at <c>organization.units</c>, both as plain columns (ADR-004 §10.2).
/// </para>
/// </summary>
public sealed class AuthorizationDbContext(DbContextOptions<AuthorizationDbContext> options)
    : DbContext(options)
{
    public const string SchemaName = "authz";

    public DbSet<RegisteredApplication> Applications => Set<RegisteredApplication>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<UserRoleAssignment> Assignments => Set<UserRoleAssignment>();

    public DbSet<PermissionVersionRow> PermissionVersion => Set<PermissionVersionRow>();

    public DbSet<ApplicationCredential> ApplicationCredentials => Set<ApplicationCredential>();

    public DbSet<ApplicationRoleAssignment> ApplicationAssignments =>
        Set<ApplicationRoleAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<RegisteredApplication>(entity =>
        {
            entity.ToTable("applications");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Code).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(1000);

            entity.HasIndex(e => e.Code).HasDatabaseName("ux_applications_code").IsUnique();

            entity.Ignore(e => e.DomainEvents);
        });

        modelBuilder.Entity<ApplicationCredential>(entity =>
        {
            entity.ToTable("application_credentials");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.ClientId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.SecretHash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Label).HasMaxLength(120).IsRequired();

            // The token endpoint looks a credential up by client id on every
            // machine call in the company. Unique as well as indexed: two rows
            // sharing a client id would make which secret is accepted depend on
            // which row the planner happened to read first.
            entity.HasIndex(e => e.ClientId)
                .HasDatabaseName("ux_application_credentials_client_id")
                .IsUnique();

            entity.HasIndex(e => e.ApplicationId)
                .HasDatabaseName("ix_application_credentials_application");

            entity.HasOne<RegisteredApplication>()
                .WithMany()
                .HasForeignKey(e => e.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Ignore(e => e.DomainEvents);
        });

        modelBuilder.Entity<ApplicationRoleAssignment>(entity =>
        {
            entity.ToTable("application_assignments");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.ScopeType).HasConversion<int>();

            entity.HasIndex(e => new { e.ApplicationId, e.RoleId })
                .HasDatabaseName("ix_application_assignments_application_role");

            entity.HasOne<RegisteredApplication>()
                .WithMany()
                .HasForeignKey(e => e.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Role>()
                .WithMany()
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Ignore(e => e.DomainEvents);
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.ToTable("permissions");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Name).HasMaxLength(PermissionName.MaxLength).IsRequired();
            entity.Property(e => e.Application).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Resource).HasMaxLength(48).IsRequired();
            entity.Property(e => e.Action).HasMaxLength(48).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500).IsRequired();

            // The lookup on every permission check that misses the cache.
            entity.HasIndex(e => e.Name).HasDatabaseName("ux_permissions_name").IsUnique();

            // Serves the administration portal, which groups by application.
            entity.HasIndex(e => new { e.ApplicationId, e.Resource })
                  .HasDatabaseName("ix_permissions_application_resource");

            entity.HasOne<RegisteredApplication>()
                  .WithMany()
                  .HasForeignKey(e => e.ApplicationId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("roles");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Code).HasMaxLength(64).IsRequired();
            entity.Property(e => e.NameAr).HasMaxLength(200).IsRequired();
            entity.Property(e => e.NameEn).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(1000);

            entity.HasIndex(e => e.Code).HasDatabaseName("ux_roles_code").IsUnique();

            // The permissions collection is owned by the role, so EF loads and
            // saves them together — a role and its permissions are one
            // consistency boundary.
            entity.HasMany(e => e.Permissions)
                  .WithOne()
                  .HasForeignKey(rp => rp.RoleId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.Navigation(e => e.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);

            entity.Ignore(e => e.DomainEvents);
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.ToTable("role_permissions");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();

            // One grant per permission per role.
            entity.HasIndex(e => new { e.RoleId, e.PermissionId })
                  .HasDatabaseName("ux_role_permissions")
                  .IsUnique();

            entity.HasOne<Permission>()
                  .WithMany()
                  .HasForeignKey(e => e.PermissionId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserRoleAssignment>(entity =>
        {
            entity.ToTable("user_role_assignments");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.ScopeType).HasConversion<int>().IsRequired();

            // The query behind every permission resolution. Filtered to active
            // grants, because that is the only shape ever asked for.
            entity.HasIndex(e => new { e.UserId, e.RevokedAt })
                  .HasDatabaseName("ix_assignments_user_active");

            entity.HasIndex(e => e.RoleId).HasDatabaseName("ix_assignments_role");

            entity.HasOne<Role>()
                  .WithMany()
                  .HasForeignKey(e => e.RoleId)
                  .OnDelete(DeleteBehavior.Restrict);

            // NO foreign key on UserId or ScopeUnitId. They point into the
            // identity and organization schemas, and a key across either
            // boundary would make those modules unextractable.

            entity.Ignore(e => e.DomainEvents);
        });

        modelBuilder.Entity<PermissionVersionRow>(entity =>
        {
            entity.ToTable("permission_version");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Version).IsConcurrencyToken();
        });

        modelBuilder.ConfigureOutbox();

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplySnakeCaseNames();
    }
}

/// <summary>
/// The single-row table holding the permission version stamp.
/// <para>
/// One row, one column that only ever increases. It lives in the database rather
/// than in memory because with several application instances an in-memory
/// counter would let instance B keep serving access that instance A revoked.
/// </para>
/// </summary>
public sealed class PermissionVersionRow
{
    /// <summary>Fixed, because there is exactly one row.</summary>
    public static readonly Guid SingletonId = Guid.ParseExact("00000000000000000000000000000001", "N");

    public Guid Id { get; set; } = SingletonId;

    public long Version { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
