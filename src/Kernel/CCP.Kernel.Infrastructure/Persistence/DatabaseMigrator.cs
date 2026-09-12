using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CCP.Kernel.Infrastructure.Persistence;

/// <summary>How the database schema reaches a deployed environment.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Whether the application applies pending migrations as it starts.
    /// <para>
    /// <b>Off by default.</b> On a serious deployment, migrations run as a
    /// separate, reviewable step before the new version is released, so that a
    /// schema change is a decision someone made rather than a side effect of a
    /// restart. Turning it on trades that review for the ability to deploy a
    /// container with nothing else attached — which is exactly what a small
    /// managed platform offers, and why the option exists.
    /// </para>
    /// <para>
    /// It is safe to enable with several instances: the migrator takes a
    /// PostgreSQL advisory lock, so exactly one process migrates and the others
    /// wait for it rather than racing.
    /// </para>
    /// </summary>
    public bool ApplyMigrationsOnStartup { get; set; }

    /// <summary>
    /// The role the application connects as, when it is not the role that
    /// migrates.
    /// <para>
    /// A migration that tightens privileges has to name somebody. With one role
    /// for everything the answer is <c>current_user</c> and there is nothing to
    /// configure. Once the roles are separated, <c>current_user</c> is the
    /// migrator — so the audit trail's append-only grant would land on the role
    /// that never inserts an audit row, and the application would be left
    /// without the privilege it needs.
    /// </para>
    /// <para>
    /// Empty means "the role running the migration", which is the single-role
    /// deployment and the behaviour every existing environment already has.
    /// </para>
    /// </summary>
    public string? ApplicationRole { get; set; }
}

/// <summary>
/// Applies every module's migrations, in one place, under one lock.
/// <para>
/// Each module owns a schema and its own migrations history table (ADR-004
/// §10.3), so migrating means walking every registered context rather than one.
/// The kernel goes first: its <c>outbox_messages</c> table is mapped into every
/// module context and excluded from their migrations, so it must exist before
/// they run.
/// </para>
/// </summary>
public sealed class DatabaseMigrator(ILogger<DatabaseMigrator> logger)
{
    /// <summary>
    /// An arbitrary but fixed application-wide lock id. Any constant works; it
    /// only has to be the same in every instance of this application and
    /// unlikely to collide with another application sharing the server.
    /// </summary>
    private const long AdvisoryLockId = 0x4343_5020_4D49_4752; // "CCP MIGR"

    /// <summary>
    /// Migrates each context in order, holding an advisory lock for the whole
    /// run.
    /// </summary>
    /// <param name="connectionString">
    /// Where the lock is taken. This must be the <b>unhardened</b> string:
    /// <c>pg_advisory_lock</c> blocks until it is granted, and a statement
    /// timeout applies to a blocking statement, so a second instance waiting
    /// behind a long migration would have its wait cancelled and would then try
    /// to serve requests against a half-migrated schema.
    /// </param>
    /// <param name="contexts">
    /// The contexts to migrate, kernel first. Order matters only for the kernel;
    /// no foreign key crosses a schema boundary, so the modules are independent
    /// of one another.
    /// </param>
    /// <param name="applicationRole">
    /// The role the application connects as, when it is not this one. Published
    /// to each migration as <c>ccp.application_role</c>, so a migration that
    /// grants or revokes privileges can name the role that will actually be
    /// bound by them. Null or empty leaves the setting unset, and a migration
    /// falls back to <c>current_user</c> — which is correct, and is what every
    /// single-role deployment has always done.
    /// </param>
    public async Task MigrateAsync(
        string connectionString,
        IReadOnlyList<DbContext> contexts,
        string? applicationRole = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contexts);

        // The lock is held on a connection of its own, kept open for the whole
        // run. A session-scoped advisory lock lives as long as its connection,
        // so borrowing a context's connection would release it the moment that
        // context finished — which is precisely when the next one starts.
        await using var lockConnection = new NpgsqlConnection(connectionString);
        await lockConnection.OpenAsync(cancellationToken);

        logger.LogInformation("Waiting for the migration lock.");

        await using (NpgsqlCommand acquire = lockConnection.CreateCommand())
        {
            // Blocking, not try-and-skip. A second instance that failed to get
            // the lock must wait for the schema to be ready, not start serving
            // requests against a database that is still being changed.
            acquire.CommandText = "SELECT pg_advisory_lock(@id)";
            acquire.Parameters.AddWithValue("id", AdvisoryLockId);

            await acquire.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            foreach (DbContext context in contexts)
            {
                IEnumerable<string> pending = await context.Database
                    .GetPendingMigrationsAsync(cancellationToken);

                string[] toApply = [.. pending];

                if (toApply.Length == 0)
                {
                    continue;
                }

                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation(
                        "Applying {Count} migration(s) for {Context}: {Migrations}",
                        toApply.Length,
                        context.GetType().Name,
                        string.Join(", ", toApply));
                }

                // The connection is opened here and held for the whole
                // migration, so the settings below apply to it and EF reuses it
                // rather than taking a fresh one from the pool.
                await context.Database.OpenConnectionAsync(cancellationToken);

                try
                {
                    // A schema change is legitimately slow. Creating an index on
                    // a large table is minutes of honest work, and the timeouts
                    // that protect the Platform from a runaway web query would
                    // cancel it half-way through — which is a far worse outcome
                    // than the one they exist to prevent.
                    //
                    // Session-scoped, and Npgsql issues DISCARD ALL when the
                    // connection returns to the pool, so nothing leaks back into
                    // the requests that follow.
                    await context.Database.ExecuteSqlRawAsync(
                        "SET statement_timeout = 0; SET idle_in_transaction_session_timeout = 0;",
                        cancellationToken);

                    if (!string.IsNullOrWhiteSpace(applicationRole))
                    {
                        // Told to the migration rather than compiled into it,
                        // because the role's name belongs to the deployment and
                        // a migration is a file in the repository. Set through
                        // set_config with a parameter: SET takes no parameters,
                        // and a role name pasted into SQL is a role name
                        // somebody can choose.
                        await context.Database.ExecuteSqlRawAsync(
                            "SELECT set_config('ccp.application_role', {0}, false)",
                            [applicationRole.Trim()],
                            cancellationToken);
                    }

                    await context.Database.MigrateAsync(cancellationToken);
                }
                finally
                {
                    await context.Database.CloseConnectionAsync();
                }
            }

            logger.LogInformation("The database schema is up to date.");
        }
        finally
        {
            await using NpgsqlCommand release = lockConnection.CreateCommand();
            release.CommandText = "SELECT pg_advisory_unlock(@id)";
            release.Parameters.AddWithValue("id", AdvisoryLockId);

            await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
