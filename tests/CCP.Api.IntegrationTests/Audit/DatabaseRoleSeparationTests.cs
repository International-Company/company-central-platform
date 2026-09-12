using CCP.Modules.Audit.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace CCP.Api.IntegrationTests.Audit;

/// <summary>
/// The audit trail's append-only privileges land on the role that actually
/// writes audit events.
/// <para>
/// <b>This is the half of role separation that fails silently.</b> Splitting
/// migration from serving is two connection strings and a role the deployment
/// creates — visible, and wrong in an obvious way if it is wrong. What is not
/// visible is that the trail's append-only guarantee is a <c>REVOKE</c> issued by
/// a migration, and a migration naming <c>current_user</c> names <i>the
/// migrator</i>. The grant would land on the role that never inserts an audit
/// row; the application would be left not append-only but unable to append, and
/// nothing would say so until the first audited action in production.
/// </para>
/// <para>
/// So this runs the shipped migration's own SQL — read from the migration
/// object, not copied into the test, because a copy is a second thing to keep
/// right — against a throwaway role, and asks PostgreSQL what that role ended up
/// holding.
/// </para>
/// </summary>
public sealed class DatabaseRoleSeparationTests(PlatformApiFactory factory)
    : IClassFixture<PlatformApiFactory>
{
    [Fact]
    public async Task TheAppendOnlyGrantGoesToTheConfiguredRoleAndNotTheMigrator()
    {
        string role = $"ccp_test_app_{Guid.NewGuid():N}"[..24];

        await using var connection = new NpgsqlConnection(factory.TestConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        await ExecuteAsync(connection, $"CREATE ROLE \"{role}\" NOLOGIN");

        try
        {
            // The mechanism under test: the migrator publishes the application's
            // role on its session, and the migration reads it from there rather
            // than assuming it is the role running.
            await ExecuteAsync(
                connection, $"SELECT set_config('ccp.application_role', '{role}', false)");

            foreach (string sql in SqlOf(new AuditPrivilegesForApplicationRole()))
            {
                await ExecuteAsync(connection, sql);
            }

            string[] privileges = await PrivilegesAsync(connection, role);

            // Append and read. Nothing else, because a trail that can be edited
            // is a trail that proves nothing about the day somebody edited it.
            Assert.Contains("INSERT", privileges);
            Assert.Contains("SELECT", privileges);
            Assert.DoesNotContain("UPDATE", privileges);
            Assert.DoesNotContain("DELETE", privileges);
            Assert.DoesNotContain("TRUNCATE", privileges);
        }
        finally
        {
            // Privileges first: PostgreSQL refuses to drop a role that still
            // holds any, and leaving the role behind would make the next run of
            // this suite fail on a name collision that has nothing to do with
            // what it is testing.
            await ExecuteAsync(connection, $"REVOKE ALL ON ALL TABLES IN SCHEMA audit FROM \"{role}\"");
            await ExecuteAsync(connection, $"REVOKE ALL ON SCHEMA audit FROM \"{role}\"");
            await ExecuteAsync(
                connection,
                $"ALTER DEFAULT PRIVILEGES FOR ROLE {Quote(await CurrentUserAsync(connection))} "
                + $"IN SCHEMA audit REVOKE ALL ON TABLES FROM \"{role}\"");
            await ExecuteAsync(connection, $"DROP ROLE IF EXISTS \"{role}\"");
            await ExecuteAsync(connection, "SELECT set_config('ccp.application_role', '', false)");
        }
    }

    /// <summary>
    /// With nothing configured, the migration still names the role running it —
    /// which is the single-role deployment every existing environment is, and
    /// which must go on behaving exactly as it did.
    /// </summary>
    [Fact]
    public async Task WithNoRoleConfiguredTheGrantStillLandsOnTheMigrator()
    {
        await using var connection = new NpgsqlConnection(factory.TestConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        await ExecuteAsync(connection, "SELECT set_config('ccp.application_role', '', false)");

        foreach (string sql in SqlOf(new AuditPrivilegesForApplicationRole()))
        {
            await ExecuteAsync(connection, sql);
        }

        string[] privileges = await PrivilegesAsync(connection, await CurrentUserAsync(connection));

        Assert.Contains("INSERT", privileges);
        Assert.Contains("SELECT", privileges);
    }

    // -----------------------------------------------------------------------
    // Harness
    // -----------------------------------------------------------------------

    /// <summary>
    /// The migration's own statements, taken from the migration rather than
    /// transcribed. A transcription is a second copy of a security rule, and the
    /// two diverge the first time one of them is edited.
    /// </summary>
    private static IEnumerable<string> SqlOf(Migration migration)
        => migration.UpOperations.OfType<SqlOperation>().Select(operation => operation.Sql);

    /// <summary>
    /// Asked of the server rather than read off the connection string, which may
    /// not name a user at all.
    /// </summary>
    private static async Task<string> CurrentUserAsync(NpgsqlConnection connection)
    {
        await using NpgsqlCommand command = connection.CreateCommand();

        command.CommandText = "SELECT current_user";

        return (string)(await command.ExecuteScalarAsync(CancellationToken.None))!;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = connection.CreateCommand();

        command.CommandText = sql;

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<string[]> PrivilegesAsync(NpgsqlConnection connection, string role)
    {
        await using NpgsqlCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT DISTINCT privilege_type
            FROM information_schema.table_privileges
            WHERE grantee = @role AND table_schema = 'audit'
            """;

        command.Parameters.AddWithValue("role", role);

        var privileges = new List<string>();

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(CancellationToken.None);

        while (await reader.ReadAsync(CancellationToken.None))
        {
            privileges.Add(reader.GetString(0));
        }

        return [.. privileges];
    }

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
