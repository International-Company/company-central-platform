using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CCP.Kernel.Infrastructure.Persistence;

/// <summary>
/// Builds a <see cref="KernelDbContext"/> for design-time tooling
/// (<c>dotnet ef migrations add</c>, <c>dotnet ef database update</c>).
/// <para>
/// Without this, the tooling has to boot the whole host, which needs a real
/// connection string and would make generating a migration depend on having a
/// configured environment. Migrations are source code and should be generatable
/// on any developer machine.
/// </para>
/// <para>
/// The connection string here is used only to determine the provider and its
/// version behaviour when scaffolding SQL. It is never used at runtime, and the
/// value below reaches no real database.
/// </para>
/// </summary>
public sealed class KernelDbContextFactory : IDesignTimeDbContextFactory<KernelDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=ccp_design_time;Username=design;Password=design";

    public KernelDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("CCP_ConnectionStrings__Platform")
            ?? DesignTimeConnectionString;

        DbContextOptions<KernelDbContext> options =
            new DbContextOptionsBuilder<KernelDbContext>()
                .UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__ef_migrations_history", KernelDbContext.SchemaName))
                .Options;

        return new KernelDbContext(options);
    }
}
