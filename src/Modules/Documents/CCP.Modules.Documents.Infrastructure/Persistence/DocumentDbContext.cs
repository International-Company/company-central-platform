using CCP.Kernel.Infrastructure.Outbox;
using CCP.Kernel.Infrastructure.Persistence;
using CCP.Modules.Documents.Domain.Access;
using CCP.Modules.Documents.Domain.Auditing;
using CCP.Modules.Documents.Domain.Documents;
using CCP.Modules.Documents.Domain.Linking;
using Microsoft.EntityFrameworkCore;

namespace CCP.Modules.Documents.Infrastructure.Persistence;

/// <summary>
/// The <c>documents</c> schema.
/// <para>
/// One DbContext and one migration history per module (ADR-004). <b>No foreign
/// key leaves this schema</b> — the owner, the subject of an access rule and the
/// organizational unit are all deliberately just columns, and the resource a
/// link points at is two strings belonging to a system that may not exist yet.
/// </para>
/// </summary>
public sealed class DocumentDbContext(DbContextOptions<DocumentDbContext> options)
    : DbContext(options)
{
    public const string SchemaName = "documents";

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentVersion> Versions => Set<DocumentVersion>();

    public DbSet<DocumentAccessRule> AccessRules => Set<DocumentAccessRule>();

    public DbSet<DocumentLink> Links => Set<DocumentLink>();

    public DbSet<DocumentAccessLog> AccessLog => Set<DocumentAccessLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        ConfigureDocuments(modelBuilder);
        ConfigureVersions(modelBuilder);
        ConfigureAccessRules(modelBuilder);
        ConfigureLinks(modelBuilder);
        ConfigureAccessLog(modelBuilder);

        modelBuilder.ConfigureOutbox();

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplySnakeCaseNames();
    }

    private static void ConfigureDocuments(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Document>(entity =>
        {
            entity.ToTable("documents");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Title).HasMaxLength(300).IsRequired();
            entity.Property(e => e.Category).HasMaxLength(100);
            entity.Property(e => e.Status).HasConversion<int>();

            // The listing query: what is in this unit, newest first.
            entity.HasIndex(e => new { e.OrganizationUnitId, e.Status, e.CreatedAt })
                .HasDatabaseName("ix_documents_unit");

            // "What did I upload?", which is the first thing anybody asks.
            entity.HasIndex(e => new { e.OwnerUserId, e.CreatedAt })
                .HasDatabaseName("ix_documents_owner");

            // The purge sweep, which runs on a timer and wants the handful of
            // documents whose grace period has run out. Partial, so its cost
            // stays proportional to what is pending deletion rather than to
            // every document the company has ever stored.
            entity.HasIndex(e => e.PurgeAfter)
                .HasDatabaseName("ix_documents_purge_due")
                .HasFilter("status = 2");

            entity.HasMany(e => e.Versions)
                .WithOne()
                .HasForeignKey(v => v.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Navigation(e => e.Versions).AutoInclude();

            entity.Ignore(e => e.DomainEvents);
            entity.Ignore(e => e.CurrentVersion);
        });

    private static void ConfigureVersions(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<DocumentVersion>(entity =>
        {
            entity.ToTable("versions");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.FileName).HasMaxLength(400).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(160).IsRequired();
            entity.Property(e => e.Sha256).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ObjectKey).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(1000);

            entity.HasIndex(e => new { e.DocumentId, e.VersionNumber })
                .HasDatabaseName("ux_versions_document_number")
                .IsUnique();

            // Two documents must never share an object key, or purging one
            // destroys the other. The database says so rather than trusting the
            // random generator to be lucky forever.
            entity.HasIndex(e => e.ObjectKey)
                .HasDatabaseName("ux_versions_object_key")
                .IsUnique();
        });

    private static void ConfigureAccessRules(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<DocumentAccessRule>(entity =>
        {
            entity.ToTable("access_rules");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.SubjectKind).HasConversion<int>();
            entity.Property(e => e.Level).HasConversion<int>();

            entity.HasIndex(e => e.DocumentId)
                .HasDatabaseName("ix_access_rules_document");

            // One rule per subject per document. Two would make "what may this
            // person do" depend on which row was read first.
            entity.HasIndex(e => new { e.DocumentId, e.SubjectKind, e.SubjectId })
                .HasDatabaseName("ux_access_rules_document_subject")
                .IsUnique();

            // The search: which documents reach this subject. Read in the
            // opposite direction from the check above, and frequent enough to
            // deserve its own index.
            entity.HasIndex(e => new { e.SubjectKind, e.SubjectId })
                .HasDatabaseName("ix_access_rules_subject");

            entity.Ignore(e => e.DomainEvents);
        });

    private static void ConfigureLinks(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<DocumentLink>(entity =>
        {
            entity.ToTable("links");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.ResourceType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ResourceId).HasMaxLength(200).IsRequired();

            entity.HasIndex(e => new { e.DocumentId, e.ResourceType, e.ResourceId })
                .HasDatabaseName("ux_links_document_resource")
                .IsUnique();

            // "What is filed against this order?" — the question a business
            // system asks on every screen that shows attachments.
            entity.HasIndex(e => new { e.ResourceType, e.ResourceId })
                .HasDatabaseName("ix_links_resource");

            entity.Ignore(e => e.DomainEvents);
        });

    private static void ConfigureAccessLog(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<DocumentAccessLog>(entity =>
        {
            entity.ToTable("access_log");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Action).HasConversion<int>();
            entity.Property(e => e.Detail).HasMaxLength(1000);
            entity.Property(e => e.IpAddress).HasMaxLength(64);
            entity.Property(e => e.UserAgent).HasMaxLength(400);

            entity.HasIndex(e => new { e.DocumentId, e.OccurredAt })
                .HasDatabaseName("ix_access_log_document");

            // "What has this person been reading?" — asked during an
            // investigation, and unanswerable in reasonable time without this.
            entity.HasIndex(e => new { e.ActorUserId, e.OccurredAt })
                .HasDatabaseName("ix_access_log_actor");

            // No cascade from documents. The log outlives the content on
            // purpose, and a cascade would delete the evidence along with the
            // thing it is evidence about.
            entity.HasIndex(e => e.WasAllowed)
                .HasDatabaseName("ix_access_log_denied")
                .HasFilter("was_allowed = false");
        });
}
