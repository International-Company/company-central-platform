using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CCP.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Builds an <see cref="IdentityDbContext"/> for design-time tooling, so
/// migrations can be generated on any machine without a configured environment.
/// The connection string here only tells the provider which SQL to scaffold; it
/// reaches no real database.
/// </summary>
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=ccp_design_time;Username=design;Password=design";

    public IdentityDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("CCP_ConnectionStrings__Platform")
            ?? DesignTimeConnectionString;

        DbContextOptions<IdentityDbContext> options =
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.SchemaName))
                .Options;

        return new IdentityDbContext(options);
    }
}
