using System.Globalization;
using CCP.Kernel.Infrastructure.Persistence;
using CCP.Modules.Audit.Infrastructure.Persistence;
using CCP.Modules.Documents.Infrastructure.Persistence;
using CCP.Modules.Workflow.Infrastructure.Persistence;
using CCP.Modules.Notifications.Infrastructure.Persistence;
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
public class PlatformApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
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
        => builder.UseEnvironment("Development");

    /// <summary>
    /// Publishes this test's settings as environment variables.
    /// <para>
    /// <b>Environment variables, not <c>ConfigureAppConfiguration</c>.</b> The
    /// composition root reads the connection string straight off
    /// <c>builder.Configuration</c> before <c>builder.Build()</c> is ever
    /// called, so that it can fail fast on a misconfigured deployment. Anything
    /// the factory contributes through <c>ConfigureAppConfiguration</c> is
    /// applied while the host is being built — which is after that read has
    /// already happened and thrown.
    /// </para>
    /// <para>
    /// The host does call <c>AddEnvironmentVariables(prefix: "CCP_")</c> before
    /// the read, so setting them here reaches the code that needs them. This is
    /// also closer to how a real deployment supplies configuration
    /// (ARCHITECTURE.md §12.7), which makes the test path and the production
    /// path the same path.
    /// </para>
    /// <para>
    /// Called from <c>InitializeAsync</c> rather than from
    /// <c>ConfigureWebHost</c> so the ordering is unambiguous: it happens before
    /// any client, and therefore before any host, exists.
    /// </para>
    /// </summary>
    private void PublishConfiguration()
    {
        Environment.SetEnvironmentVariable("CCP_ConnectionStrings__Platform", TestConnectionString);

        // A short poll keeps outbox tests fast without sleeping.
        Environment.SetEnvironmentVariable("CCP_Outbox__PollInterval", "00:00:01");

        Environment.SetEnvironmentVariable(
            "CCP_RateLimits__Authentication",
            AuthenticationRateLimit.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The authentication budget this suite runs under.
    /// <para>
    /// Raised far above the production default, because every test in the
    /// process reaches the server from one address and therefore shares one
    /// partition. At the real limit the suite exhausts its budget after ten
    /// sign-ins and everything afterwards fails with 429 — which says nothing
    /// about the code under test.
    /// </para>
    /// <para>
    /// The limiter is not thereby left untested: <see cref="RateLimitTests"/>
    /// overrides this with a small number and asserts that the rejection, and
    /// its <c>Retry-After</c> header, actually happen.
    /// </para>
    /// <para>
    /// That the suite tripped this at all is worth recording rather than merely
    /// working around. A test process behind one address is exactly what an
    /// office behind one NAT looks like, and the production limit has the same
    /// problem for real users (DEVELOPMENT_STATUS.md §7, debt #23).
    /// </para>
    /// </summary>
    protected virtual int AuthenticationRateLimit => 10_000;

    // Explicit interface implementation: xUnit's IAsyncLifetime declares
    // Task-returning members, while WebApplicationFactory already defines a
    // ValueTask-returning DisposeAsync. Implementing the interface explicitly
    // lets both coexist without either being renamed.
    async Task IAsyncLifetime.InitializeAsync()
    {
        await CreateDatabaseAsync();
        await ApplyMigrationsAsync();

        PublishConfiguration();
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
        await MigrateAsync<AuditDbContext>(AuditDbContext.SchemaName, o => new AuditDbContext(o));
        await MigrateAsync<WorkflowDbContext>(
            WorkflowDbContext.SchemaName, o => new WorkflowDbContext(o));
        await MigrateAsync<NotificationDbContext>(
            NotificationDbContext.SchemaName, o => new NotificationDbContext(o));
        await MigrateAsync<DocumentDbContext>(
            DocumentDbContext.SchemaName, o => new DocumentDbContext(o));
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
