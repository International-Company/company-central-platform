using CCP.Kernel.Infrastructure.Persistence;
using CCP.Modules.Authorization.Infrastructure.Persistence;
using CCP.Modules.Identity.Infrastructure.Persistence;
using CCP.Modules.Organization.Infrastructure.Persistence;
using CCP.Modules.Security.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Api.IntegrationTests;

/// <summary>
/// Boots the real host for integration tests.
/// <para>
/// The tests run against a real PostgreSQL database, not an in-memory
/// substitute. An in-memory provider does not enforce constraints, does not
/// support <c>FOR UPDATE SKIP LOCKED</c>, and does not behave like PostgreSQL
/// under concurrency — which is precisely what the outbox needs proving
/// against.
/// </para>
/// <para>
/// Each test class gets its own database, created from migrations and dropped
/// afterwards, so tests cannot interfere with one another.
/// </para>
/// </summary>
public sealed class PlatformApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _databaseName = $"ccp_test_{Guid.NewGuid():N}";

    /// <summary>
    /// Connection to the local PostgreSQL instance. Overridable so the same
    /// suite runs against Docker Compose, a native install, or CI.
    /// </summary>
    private static string AdminConnectionString =>
        Environment.GetEnvironmentVariable("CCP_TEST_POSTGRES")
        ?? "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=ccp_dev_local_only";

    public string TestConnectionString =>
        AdminConnectionString.Replace(
            "Database=postgres",
            $"Database={_databaseName}",
            StringComparison.Ordinal);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Platform"] = TestConnectionString,

                // A short poll keeps outbox tests fast without sleeping.
                ["Outbox:PollInterval"] = "00:00:01"
            }));
    }

    // Explicit interface implementation: xUnit's IAsyncLifetime declares
    // Task-returning members, while WebApplicationFactory already defines a
    // ValueTask-returning DisposeAsync. Implementing the interface explicitly
    // lets both coexist without either being renamed.
    async Task IAsyncLifetime.InitializeAsync()
    {
        await CreateDatabaseAsync();
        await ApplyMigrationsAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await DropDatabaseAsync();
        GC.SuppressFinalize(this);
    }

    private async Task CreateDatabaseAsync()
    {
        await using var connection = new Npgsql.NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        // The database name is generated here, never supplied by a caller, and
        // CREATE DATABASE cannot be parameterized.
        command.CommandText = $"CREATE DATABASE \"{_databaseName}\"";
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Applies every module's migrations, not just the kernel's.
    /// <para>
    /// One context per schema, each with its own migrations history table, which
    /// is what a real deployment does (ADR-004 §10.3). Migrating only the kernel
    /// would leave every module schema absent and every module test failing on a
    /// missing table — a failure mode that says nothing about the code under
    /// test.
    /// </para>
    /// <para>
    /// The kernel goes first because its <c>outbox_messages</c> table is mapped
    /// into every module context and excluded from their migrations; the module
    /// order after that is not significant, since no foreign key crosses a
    /// schema boundary.
    /// </para>
    /// </summary>
    private async Task ApplyMigrationsAsync()
    {
        // Migrations, not EnsureCreated: the tests must exercise the same schema
        // a deployment produces, including its indexes and constraints.
        await MigrateAsync<KernelDbContext>(KernelDbContext.SchemaName, o => new KernelDbContext(o));
        await MigrateAsync<IdentityDbContext>(IdentityDbContext.SchemaName, o => new IdentityDbContext(o));
        await MigrateAsync<OrganizationDbContext>(
            OrganizationDbContext.SchemaName, o => new OrganizationDbContext(o));
        await MigrateAsync<AuthorizationDbContext>(
            AuthorizationDbContext.SchemaName, o => new AuthorizationDbContext(o));
        await MigrateAsync<SecurityDbContext>(SecurityDbContext.SchemaName, o => new SecurityDbContext(o));
    }

    private async Task MigrateAsync<TContext>(
        string schemaName,
        Func<DbContextOptions<TContext>, TContext> factory)
        where TContext : DbContext
    {
        DbContextOptions<TContext> options =
            new DbContextOptionsBuilder<TContext>()
                .UseNpgsql(TestConnectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__ef_migrations_history", schemaName))
                .Options;

        await using TContext context = factory(options);

        await context.Database.MigrateAsync();
    }

    private async Task DropDatabaseAsync()
    {
        await using var connection = new Npgsql.NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();

        await using var terminate = connection.CreateCommand();
        terminate.CommandText =
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @name AND pid <> pg_backend_pid()";
        terminate.Parameters.AddWithValue("name", _databaseName);
        await terminate.ExecuteNonQueryAsync();

        await using var drop = connection.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\"";
        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>Opens a context against this test's database.</summary>
    public KernelDbContext CreateDbContext()
    {
        DbContextOptions<KernelDbContext> options =
            new DbContextOptionsBuilder<KernelDbContext>()
                .UseNpgsql(TestConnectionString)
                .Options;

        return new KernelDbContext(options);
    }
}
