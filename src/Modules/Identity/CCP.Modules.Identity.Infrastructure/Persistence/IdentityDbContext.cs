using CCP.Kernel.Infrastructure.Outbox;
using CCP.Kernel.Infrastructure.Persistence;
using CCP.Modules.Identity.Domain.Credentials;
using CCP.Modules.Identity.Domain.Sessions;
using CCP.Modules.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CCP.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// The <c>identity</c> schema.
/// <para>
/// One DbContext and one migration history per module, so modules version
/// independently (ADR-004). <b>No foreign key leaves this schema</b> — that rule
/// is what keeps the module extractable, and it is asserted by an architecture
/// test.
/// </para>
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public const string SchemaName = "identity";

    public DbSet<User> Users => Set<User>();

    public DbSet<UserCredential> Credentials => Set<UserCredential>();

    public DbSet<PasswordHistoryEntry> PasswordHistory => Set<PasswordHistoryEntry>();

    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        ConfigureUsers(modelBuilder);
        ConfigureCredentials(modelBuilder);
        ConfigureSessions(modelBuilder);
        ConfigureRefreshTokens(modelBuilder);
        ConfigureLoginAttempts(modelBuilder);

        // The outbox is mapped into this context so a staged event commits in
        // the same transaction as the change that produced it. Writing it
        // through a different context would make those two transactions, and an
        // event could then be lost or recorded for a rollback.
        modelBuilder.ConfigureOutbox();

        base.OnModelCreating(modelBuilder);

        // Applied last so it rewrites every name configured above.
        modelBuilder.ApplySnakeCaseNames();
    }

    private static void ConfigureUsers(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Username).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(256).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(128).IsRequired();

            // Two characters, and nullable. Null means "never chose", which is
            // a different fact from "chose the company default" -- the first
            // follows the company if it changes its language and the second
            // does not.
            entity.Property(e => e.PreferredLocale).HasMaxLength(8);
            entity.Property(e => e.Status).HasConversion<int>().IsRequired();

            // Username and email are normalised to lower case by the domain on
            // write, so ordinary unique indexes give case-insensitive
            // uniqueness with no functional index to maintain. Enforced by the
            // database, not only by a check in code that a future path might
            // skip (ARCHITECTURE.md §10.4).
            entity.HasIndex(e => e.Username)
                  .HasDatabaseName("ux_users_username")
                  .IsUnique();

            entity.HasIndex(e => e.Email)
                  .HasDatabaseName("ux_users_email")
                  .IsUnique();

            entity.HasIndex(e => e.Status).HasDatabaseName("ix_users_status");

            // Domain events are dispatched through the outbox, never stored.
            entity.Ignore(e => e.DomainEvents);
        });

    private static void ConfigureCredentials(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserCredential>(entity =>
        {
            entity.ToTable("user_credentials");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.PasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(e => e.Algorithm).HasMaxLength(32).IsRequired();

            // Exactly one current credential per user.
            entity.HasIndex(e => e.UserId).HasDatabaseName("ux_user_credentials_user").IsUnique();

            entity.HasOne<User>()
                  .WithOne()
                  .HasForeignKey<UserCredential>(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.ToTable("password_reset_tokens");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.RequestedFromIp).HasMaxLength(45);

            // The lookup when a token is redeemed. Unique, because a collision
            // would mean one link resetting the wrong account.
            entity.HasIndex(e => e.TokenHash)
                  .HasDatabaseName("ux_password_reset_tokens_hash")
                  .IsUnique();

            // Finds a user's outstanding tokens when a new one supersedes them.
            entity.HasIndex(e => new { e.UserId, e.UsedAt, e.InvalidatedAt })
                  .HasDatabaseName("ix_password_reset_tokens_user_active");

            entity.HasOne<User>()
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PasswordHistoryEntry>(entity =>
        {
            entity.ToTable("password_history");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.PasswordHash).HasMaxLength(512).IsRequired();

            // Supports "the last N for this user", which is the only query.
            entity.HasIndex(e => new { e.UserId, e.CreatedAt })
                  .HasDatabaseName("ix_password_history_user_created");

            entity.HasOne<User>()
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureSessions(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Session>(entity =>
        {
            entity.ToTable("sessions");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.IpAddress).HasMaxLength(45);       // IPv6
            entity.Property(e => e.UserAgent).HasMaxLength(512);
            entity.Property(e => e.DeviceFingerprint).HasMaxLength(128);
            entity.Property(e => e.RevokedReason).HasMaxLength(64);

            // Serves the "my sessions" view: a user's active sessions only.
            entity.HasIndex(e => new { e.UserId, e.RevokedAt })
                  .HasDatabaseName("ix_sessions_user_active");

            entity.HasOne<User>()
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.Ignore(e => e.DomainEvents);
        });

    private static void ConfigureRefreshTokens(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.RevokedReason).HasMaxLength(64);

            // The lookup on every refresh. Unique because a hash collision here
            // would mean two sessions sharing one credential.
            entity.HasIndex(e => e.TokenHash)
                  .HasDatabaseName("ux_refresh_tokens_hash")
                  .IsUnique();

            // Loaded when reuse is detected, to revoke the whole family at once.
            entity.HasIndex(e => e.FamilyId).HasDatabaseName("ix_refresh_tokens_family");

            // Loaded at sign-out, to revoke every token of the session.
            entity.HasIndex(e => e.SessionId).HasDatabaseName("ix_refresh_tokens_session");

            entity.HasOne<Session>()
                  .WithMany()
                  .HasForeignKey(e => e.SessionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

    private static void ConfigureLoginAttempts(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<LoginAttempt>(entity =>
        {
            entity.ToTable("login_attempts");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.AttemptedUsername).HasMaxLength(64).IsRequired();
            entity.Property(e => e.FailureReason).HasMaxLength(64);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasMaxLength(512);

            entity.HasIndex(e => new { e.UserId, e.OccurredAt })
                  .HasDatabaseName("ix_login_attempts_user_time");

            // Detects a burst of failures from one address across many
            // usernames, which is what credential stuffing looks like.
            entity.HasIndex(e => new { e.IpAddress, e.OccurredAt })
                  .HasDatabaseName("ix_login_attempts_ip_time");

            // Deliberately NO foreign key to users: attempts against usernames
            // that do not exist carry a null UserId and must still be recorded.
            // A required relationship here would silently drop exactly the rows
            // that matter most for detecting an attack.
        });
}
