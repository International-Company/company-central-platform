using CCP.Modules.Audit.Domain;
using CCP.Modules.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CCP.Api.IntegrationTests.Audit;

/// <summary>
/// That the audit trail really is append-only, and partitioned.
/// <para>
/// These are the claims of ARCHITECTURE.md §15.4, and exactly the kind that
/// cannot be checked without a database. A unit test can show that no code path
/// issues an UPDATE; only PostgreSQL can show that one would be refused.
/// </para>
/// <para>
/// <b>A superuser bypasses privilege checks entirely.</b> CI connects as
/// <c>postgres</c>, so simply issuing an UPDATE here would succeed and prove
/// nothing — worse, it would look like the design had failed when it had not
/// been exercised at all. The refusal tests therefore <c>SET ROLE</c> to an
/// ordinary role first, which is what a deployment actually runs as.
/// </para>
/// </summary>
public sealed class AuditImmutabilityTests(PlatformApiFactory factory) : IClassFixture<PlatformApiFactory>
{
    /// <summary>A role with no special standing, created for these tests.</summary>
    private const string ProbeRole = "ccp_audit_probe";

    [Fact]
    public async Task TheTable_IsRangePartitioned()
    {
        await using NpgsqlConnection connection = await OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT partstrat
            FROM pg_partitioned_table
            JOIN pg_class ON pg_class.oid = pg_partitioned_table.partrelid
            JOIN pg_namespace ON pg_namespace.oid = pg_class.relnamespace
            WHERE pg_namespace.nspname = 'audit' AND pg_class.relname = 'audit_events';
            """;

        // 'r' is range partitioning. Without it, retention would have to DELETE
        // rows — and a trail you can delete from row by row is not append-only,
        // whatever the code says.
        Assert.Equal("r", (await command.ExecuteScalarAsync())?.ToString());
    }

    [Fact]
    public async Task TheMigration_GrantsInsertAndSelectAndNothingElse()
    {
        await using NpgsqlConnection connection = await OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();

        // The explicit grants recorded on the table, which is what the migration
        // actually changed. This is the assertion that tests our own work rather
        // than PostgreSQL's.
        command.CommandText =
            """
            SELECT DISTINCT privilege_type
            FROM information_schema.role_table_grants
            WHERE table_schema = 'audit' AND table_name = 'audit_events'
            ORDER BY privilege_type;
            """;

        var granted = new List<string>();

        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                granted.Add(reader.GetString(0));
            }
        }

        Assert.Equal(["INSERT", "SELECT"], granted);
    }

    [Fact]
    public async Task AnEventCanBeAppendedAndReadBack()
    {
        AuditEvent appended = await AppendAsync("audit.append.probe");

        await using AuditDbContext context = CreateContext();

        AuditEvent? found = await context.AuditEvents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == appended.Id);

        Assert.NotNull(found);
        Assert.Equal("audit.append.probe", found.Action);
    }

    [Fact]
    public async Task AnAppendedEventLandsInAMonthlyPartition()
    {
        AuditEvent appended = await AppendAsync("audit.partition.probe");

        await using NpgsqlConnection connection = await OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();

        command.CommandText = "SELECT tableoid::regclass::text FROM audit.audit_events WHERE id = @id";
        command.Parameters.AddWithValue("id", appended.Id);

        string? partition = (await command.ExecuteScalarAsync())?.ToString();

        Assert.NotNull(partition);

        // Not the default partition. A row landing there means maintenance never
        // created the month it belongs to — recoverable, but worth failing a
        // test over rather than discovering months later.
        Assert.DoesNotContain("default", partition, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnUpdateIsRefusedForAnOrdinaryRole()
    {
        AuditEvent appended = await AppendAsync("audit.update.probe");

        PostgresException exception = await AssertRefusedAsync(
            "UPDATE audit.audit_events SET action = 'tampered' WHERE id = @id", appended.Id);

        // The point of the whole design. If this ever succeeds, the trail is a
        // log with good intentions rather than evidence.
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);

        await AssertUnchangedAsync(appended);
    }

    [Fact]
    public async Task ADeleteIsRefusedForAnOrdinaryRole()
    {
        AuditEvent appended = await AppendAsync("audit.delete.probe");

        PostgresException exception = await AssertRefusedAsync(
            "DELETE FROM audit.audit_events WHERE id = @id", appended.Id);

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);

        await AssertUnchangedAsync(appended);
    }

    [Fact]
    public async Task ATruncateIsRefusedForAnOrdinaryRole()
    {
        AuditEvent appended = await AppendAsync("audit.truncate.probe");

        // The operation that would remove everything at once — and the one a
        // revocation of UPDATE and DELETE alone would not cover.
        await AssertRefusedAsync("TRUNCATE audit.audit_events", appended.Id, parameterUsed: false);

        await AssertUnchangedAsync(appended);
    }

    /// <summary>
    /// Runs a statement as an ordinary role and asserts PostgreSQL refuses it.
    /// </summary>
    private async Task<PostgresException> AssertRefusedAsync(
        string sql, Guid id, bool parameterUsed = true)
    {
        await using NpgsqlConnection connection = await OpenAsync();

        await EnsureProbeRoleAsync(connection);

        await using (NpgsqlCommand setRole = connection.CreateCommand())
        {
            // Dropping to an ordinary role for the rest of this connection. A
            // superuser is exempt from every privilege check, so without this
            // the statement below would succeed and the test would be theatre.
            setRole.CommandText = $"SET ROLE {ProbeRole}";
            await setRole.ExecuteNonQueryAsync();
        }

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;

        if (parameterUsed)
        {
            command.Parameters.AddWithValue("id", id);
        }

        return await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
    }

    /// <summary>
    /// Creates a role holding exactly what the migration grants the application:
    /// <c>INSERT</c> and <c>SELECT</c> on the audit schema, and nothing else.
    /// </summary>
    private static async Task EnsureProbeRoleAsync(NpgsqlConnection connection)
    {
        await using NpgsqlCommand command = connection.CreateCommand();

        command.CommandText =
            $"""
             DO $$
             BEGIN
                 IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{ProbeRole}') THEN
                     CREATE ROLE {ProbeRole};
                 END IF;
             END $$;

             GRANT USAGE ON SCHEMA audit TO {ProbeRole};
             GRANT INSERT, SELECT ON ALL TABLES IN SCHEMA audit TO {ProbeRole};
             """;

        await command.ExecuteNonQueryAsync();
    }

    private async Task AssertUnchangedAsync(AuditEvent appended)
    {
        await using AuditDbContext context = CreateContext();

        AuditEvent? found = await context.AuditEvents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == appended.Id);

        Assert.NotNull(found);
        Assert.Equal(appended.Action, found.Action);
    }

    private async Task<AuditEvent> AppendAsync(string action)
    {
        AuditEvent auditEvent = AuditEvent.Record(
            "platform", "tests", action, DateTimeOffset.UtcNow,
            resourceType: "probe", resourceId: Guid.NewGuid().ToString());

        await using AuditDbContext context = CreateContext();

        context.AuditEvents.Add(auditEvent);
        await context.SaveChangesAsync();

        return auditEvent;
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(factory.TestConnectionString);
        await connection.OpenAsync();

        return connection;
    }

    private AuditDbContext CreateContext()
        => new(new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(factory.TestConnectionString)
            .Options);
}
