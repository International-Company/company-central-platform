using CCP.Kernel.Infrastructure.Outbox;
using CCP.Kernel.Infrastructure.Persistence;
using CCP.Modules.Organization.Domain;
using CCP.Modules.Organization.Domain.Companies;
using CCP.Modules.Organization.Domain.Employees;
using CCP.Modules.Organization.Domain.Positions;
using CCP.Modules.Organization.Domain.Units;
using Microsoft.EntityFrameworkCore;

namespace CCP.Modules.Organization.Infrastructure.Persistence;

/// <summary>
/// The <c>organization</c> schema.
/// <para>
/// One DbContext and one migration history per module (ADR-004). <b>No foreign
/// key leaves this schema</b> — in particular <c>Employee.UserId</c> points at
/// <c>identity.users</c> and is deliberately just a column, asserted by an
/// architecture test.
/// </para>
/// </summary>
public sealed class OrganizationDbContext(DbContextOptions<OrganizationDbContext> options)
    : DbContext(options)
{
    public const string SchemaName = "organization";

    public DbSet<Company> Companies => Set<Company>();

    public DbSet<OrganizationUnit> Units => Set<OrganizationUnit>();

    public DbSet<Position> Positions => Set<Position>();

    public DbSet<Employee> Employees => Set<Employee>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        ConfigureCompanies(modelBuilder);
        ConfigureUnits(modelBuilder);
        ConfigurePositions(modelBuilder);
        ConfigureEmployees(modelBuilder);

        // Mapped so a staged event commits in the same transaction as the change
        // that produced it (ARCHITECTURE.md §8.5). Created by the kernel
        // migration, never by this module's.
        modelBuilder.ConfigureOutbox();

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplySnakeCaseNames();
    }

    private static void ConfigureCompanies(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Company>(entity =>
        {
            entity.ToTable("companies");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Code).HasMaxLength(32).IsRequired();
            entity.Property(e => e.DefaultLocale).HasMaxLength(8).IsRequired();

            MapLocalizedName(entity, e => e.Name, "name");

            entity.HasIndex(e => e.Code).HasDatabaseName("ux_companies_code").IsUnique();

            entity.Ignore(e => e.DomainEvents);
        });

    private static void ConfigureUnits(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<OrganizationUnit>(entity =>
        {
            entity.ToTable("units");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Code).HasMaxLength(32).IsRequired();
            entity.Property(e => e.UnitType).HasConversion<int>().IsRequired();

            MapLocalizedName(entity, e => e.Name, "name");

            // Long enough for a deep tree: 33 characters per level, so 1000
            // characters is about 30 levels. No real organization approaches it,
            // and the column is text rather than a fixed width anyway.
            entity.Property(e => e.Path).HasMaxLength(1000).IsRequired();

            entity.HasIndex(e => new { e.CompanyId, e.Code })
                  .HasDatabaseName("ux_units_company_code")
                  .IsUnique();

            // The index authorization depends on. Scope resolution asks
            // "every unit whose path starts with X" on every request, and
            // text_pattern_ops is what makes that a range scan rather than a
            // sequential one — the default collation's operator class cannot
            // serve a LIKE 'prefix%' query.
            entity.HasIndex(e => e.Path)
                  .HasDatabaseName("ix_units_path")
                  .HasOperators("text_pattern_ops");

            entity.HasIndex(e => e.ParentId).HasDatabaseName("ix_units_parent");

            entity.HasOne<Company>()
                  .WithMany()
                  .HasForeignKey(e => e.CompanyId)
                  .OnDelete(DeleteBehavior.Restrict);

            // Self-reference within the same schema, so this one is allowed.
            entity.HasOne<OrganizationUnit>()
                  .WithMany()
                  .HasForeignKey(e => e.ParentId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.Ignore(e => e.DomainEvents);
        });

    private static void ConfigurePositions(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Position>(entity =>
        {
            entity.ToTable("positions");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Code).HasMaxLength(32).IsRequired();

            MapLocalizedName(entity, e => e.Title, "title");

            entity.HasIndex(e => new { e.CompanyId, e.Code })
                  .HasDatabaseName("ux_positions_company_code")
                  .IsUnique();

            entity.HasOne<Company>()
                  .WithMany()
                  .HasForeignKey(e => e.CompanyId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.Ignore(e => e.DomainEvents);
        });

    private static void ConfigureEmployees(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmployeeAttribute>(entity =>
        {
            entity.ToTable("employee_attributes");
            entity.HasKey(a => a.Id);

            entity.Property(a => a.Id).ValueGeneratedNever();
            entity.Property(a => a.Key).HasMaxLength(100).IsRequired();
            entity.Property(a => a.Value).HasMaxLength(1000).IsRequired();

            // One value per key per employee, settled by the database. Two
            // applications racing to set the same key would otherwise both
            // succeed and one would silently win.
            entity.HasIndex(a => new { a.EmployeeId, a.Key })
                  .HasDatabaseName("ux_employee_attributes_key")
                  .IsUnique();
        });

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.ToTable("employees");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.EmployeeNumber).HasMaxLength(32).IsRequired();
            entity.Property(e => e.WorkEmail).HasMaxLength(256);
            entity.Property(e => e.WorkPhone).HasMaxLength(32);

            MapLocalizedName(entity, e => e.FullName, "full_name");

            entity.HasIndex(e => new { e.CompanyId, e.EmployeeNumber })
                  .HasDatabaseName("ux_employees_company_number")
                  .IsUnique();

            // One employee per user account. A partial index, so the many
            // employees with no account do not all collide on NULL.
            entity.HasIndex(e => e.UserId)
                  .HasDatabaseName("ux_employees_user")
                  .IsUnique()
                  .HasFilter("user_id IS NOT NULL");

            // Serves "everyone in this unit", which scope resolution and the
            // org chart both need.
            entity.HasIndex(e => new { e.UnitId, e.IsActive })
                  .HasDatabaseName("ix_employees_unit_active");

            entity.HasIndex(e => e.ManagerId).HasDatabaseName("ix_employees_manager");

            // Owned by the employee, cascading with them, and loaded with them.
            //
            // A child table rather than a JSON column: the keys are declared,
            // the unique index below is what stops two applications writing the
            // same one, and "what do you hold about this person" is a query
            // rather than a parse.
            entity.HasMany(e => e.Attributes)
                  .WithOne()
                  .HasForeignKey(a => a.EmployeeId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.Navigation(e => e.Attributes).UsePropertyAccessMode(PropertyAccessMode.Field);

            // Read whenever the employee is, because an attribute bag nobody
            // loaded is a bag that reads as empty -- which is indistinguishable
            // from one that is.
            entity.Navigation(e => e.Attributes).AutoInclude();

            entity.HasOne<Company>()
                  .WithMany()
                  .HasForeignKey(e => e.CompanyId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<OrganizationUnit>()
                  .WithMany()
                  .HasForeignKey(e => e.UnitId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<Position>()
                  .WithMany()
                  .HasForeignKey(e => e.PositionId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne<Employee>()
                  .WithMany()
                  .HasForeignKey(e => e.ManagerId)
                  .OnDelete(DeleteBehavior.SetNull);

            // NO foreign key on UserId. It points at identity.users, in another
            // module's schema, and a key across that boundary would make both
            // modules unextractable (ADR-004 §10.2). Integrity is enforced by
            // the use case through Identity's contract.

            entity.Ignore(e => e.DomainEvents);
        });
    }

    /// <summary>
    /// Maps a <see cref="LocalizedName"/> to two columns rather than a JSON
    /// blob, so both languages are indexable, searchable and visible to anyone
    /// querying the database directly (ADR-011 §25.3).
    /// </summary>
    private static void MapLocalizedName<TEntity>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity,
        System.Linq.Expressions.Expression<Func<TEntity, LocalizedName?>> property,
        string columnPrefix)
        where TEntity : class
        => entity.ComplexProperty(property, name =>
        {
            name.Property(n => n.Arabic)
                .HasColumnName($"{columnPrefix}_ar")
                .HasMaxLength(LocalizedName.MaxLength)
                .IsRequired();

            name.Property(n => n.English)
                .HasColumnName($"{columnPrefix}_en")
                .HasMaxLength(LocalizedName.MaxLength)
                .IsRequired();
        });
}
