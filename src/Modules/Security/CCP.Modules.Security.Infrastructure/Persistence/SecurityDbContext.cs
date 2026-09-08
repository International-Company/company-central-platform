using CCP.Kernel.Infrastructure.Outbox;
using CCP.Kernel.Infrastructure.Persistence;
using CCP.Modules.Security.Domain.Events;
using CCP.Modules.Security.Domain.Mfa;
using Microsoft.EntityFrameworkCore;

namespace CCP.Modules.Security.Infrastructure.Persistence;

/// <summary>
/// The <c>security</c> schema.
/// <para>
/// <b>No foreign key leaves this schema</b> — user ids point at
/// <c>identity.users</c> as plain columns (ADR-004 §10.2). For security events
/// that is doubly necessary: they must record attempts against usernames that do
/// not exist, and a required relationship would silently drop exactly the rows
/// that matter most.
/// </para>
/// </summary>
public sealed class SecurityDbContext(DbContextOptions<SecurityDbContext> options) : DbContext(options)
{
    public const string SchemaName = "security";

    public DbSet<MfaEnrolment> MfaEnrolments => Set<MfaEnrolment>();

    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();

    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();

    public DbSet<StepUpConfirmation> StepUpConfirmations => Set<StepUpConfirmation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<MfaEnrolment>(entity =>
        {
            entity.ToTable("mfa_enrolments");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();

            // Long enough for nonce + tag + ciphertext of a 20-byte secret,
            // base64-encoded, with room for a future algorithm change.
            entity.Property(e => e.EncryptedSecret).HasMaxLength(512).IsRequired();
            entity.Property(e => e.Status).HasConversion<int>().IsRequired();

            // One enrolment per user. Enforced by the database, because two
            // active factors would make "which one counts" ambiguous at exactly
            // the moment it must not be.
            entity.HasIndex(e => e.UserId).HasDatabaseName("ux_mfa_enrolments_user").IsUnique();

            entity.HasMany(e => e.RecoveryCodes)
                  .WithOne()
                  .HasForeignKey(c => c.EnrolmentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.Navigation(e => e.RecoveryCodes).UsePropertyAccessMode(PropertyAccessMode.Field);

            entity.Ignore(e => e.DomainEvents);
        });

        modelBuilder.Entity<RecoveryCode>(entity =>
        {
            entity.ToTable("recovery_codes");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CodeHash).HasMaxLength(64).IsRequired();

            // Redemption looks a code up by hash within an enrolment.
            entity.HasIndex(e => new { e.EnrolmentId, e.CodeHash })
                  .HasDatabaseName("ix_recovery_codes_lookup");
        });

        modelBuilder.Entity<SecurityEvent>(entity =>
        {
            entity.ToTable("security_events");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.EventType).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Severity).HasConversion<int>().IsRequired();
            entity.Property(e => e.Username).HasMaxLength(64);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasMaxLength(512);
            entity.Property(e => e.Details).HasColumnType("jsonb");
            entity.Property(e => e.CorrelationId).HasMaxLength(128);

            // The three questions actually asked of this table: what happened to
            // this account, what has this address been doing, and what is serious
            // right now. Each gets its own composite index, all leading with the
            // filter and ending with time.
            entity.HasIndex(e => new { e.UserId, e.OccurredAt })
                  .HasDatabaseName("ix_security_events_user_time");

            entity.HasIndex(e => new { e.IpAddress, e.OccurredAt })
                  .HasDatabaseName("ix_security_events_ip_time");

            entity.HasIndex(e => new { e.Severity, e.OccurredAt })
                  .HasDatabaseName("ix_security_events_severity_time");

            entity.HasIndex(e => new { e.EventType, e.OccurredAt })
                  .HasDatabaseName("ix_security_events_type_time");

            // Deliberately NO foreign key to users: events against usernames
            // that do not exist carry a null UserId and are precisely the rows
            // that reveal credential stuffing.
        });

        modelBuilder.Entity<StepUpConfirmation>(entity =>
        {
            entity.ToTable("step_up_confirmations");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();

            // The authorization check asks one question — does this session have
            // a live elevation right now — and this index answers it entirely.
            // It runs on every request to a privileged endpoint, so a scan here
            // would be felt.
            entity.HasIndex(e => new { e.SessionId, e.ExpiresAt })
                  .HasDatabaseName("ix_step_up_session_expiry");

            // Revoking on MFA change looks up by user instead.
            entity.HasIndex(e => new { e.UserId, e.ExpiresAt })
                  .HasDatabaseName("ix_step_up_user_expiry");
        });

        modelBuilder.ConfigureOutbox();

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplySnakeCaseNames();
    }
}
