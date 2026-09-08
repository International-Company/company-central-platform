using CCP.Kernel.Infrastructure.Persistence;
using CCP.Modules.Identity.Infrastructure.Persistence;
using CCP.Modules.Authorization.Infrastructure.Persistence;
using CCP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CCP.Architecture.Tests;

/// <summary>
/// Enforces the single most important database rule in the Platform:
/// <b>no foreign key crosses a module schema boundary</b> (ADR-004, §10.2).
/// <para>
/// This is what makes module extraction real rather than aspirational. A schema
/// with no inbound foreign keys can be moved to its own database in an
/// afternoon; a schema tangled in cross-module foreign keys never can. The rule
/// has a genuine cost — some integrity checks move to application code — and
/// that cost is only worth paying if the rule actually holds, which is why it is
/// asserted here rather than trusted to review.
/// </para>
/// <para>
/// The EF model is inspected rather than the database, so this runs with no
/// PostgreSQL instance and fails at build time rather than at deployment.
/// </para>
/// </summary>
public sealed class SchemaBoundaryTests
{
    /// <summary>
    /// Every module DbContext, with the schema it owns. New modules are added
    /// here as their phases complete.
    /// </summary>
    public static TheoryData<string, string> ModuleContexts() => new()
    {
        { nameof(KernelDbContext), KernelDbContext.SchemaName },
        { nameof(IdentityDbContext), IdentityDbContext.SchemaName },
        { nameof(OrganizationDbContext), OrganizationDbContext.SchemaName },
        { nameof(AuthorizationDbContext), AuthorizationDbContext.SchemaName }
    };

    [Theory]
    [MemberData(nameof(ModuleContexts))]
    public void NoForeignKeyCrossesASchemaBoundary(string contextName, string ownSchema)
    {
        IModel model = BuildModel(contextName);

        var violations = new List<string>();

        foreach (IEntityType entityType in model.GetEntityTypes())
        {
            string? principalSchemaOwner = entityType.GetSchema();

            foreach (IForeignKey foreignKey in entityType.GetForeignKeys())
            {
                string? dependentSchema = entityType.GetSchema() ?? ownSchema;
                string? principalSchema = foreignKey.PrincipalEntityType.GetSchema() ?? ownSchema;

                if (!string.Equals(dependentSchema, principalSchema, StringComparison.Ordinal))
                {
                    violations.Add(
                        $"{entityType.GetTableName()} ({dependentSchema}) → "
                        + $"{foreignKey.PrincipalEntityType.GetTableName()} ({principalSchema})");
                }
            }

            _ = principalSchemaOwner;
        }

        Assert.True(
            violations.Count == 0,
            $"{contextName} declares foreign keys that cross a schema boundary, which makes the "
            + "owning module unextractable (ADR-004 §10.2). Reference by id instead, and enforce "
            + "integrity in the application layer:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData(nameof(IdentityDbContext))]
    [InlineData(nameof(OrganizationDbContext))]
    [InlineData(nameof(AuthorizationDbContext))]
    public void ModuleContexts_DoNotGenerateTheOutboxTable(string contextName)
    {
        // Every module maps kernel.outbox_messages so events commit with the
        // change that produced them, but only the kernel migration may create
        // it. Without ExcludeFromMigrations, each module would emit its own
        // CREATE TABLE for the same table and the second migration would fail.
        // The runtime model is read-optimised and drops migration metadata, so
        // the design-time model is required to see the exclusion.
        IModel model = BuildDesignTimeModel(contextName);

        IEntityType? outbox = model.GetEntityTypes()
            .FirstOrDefault(e => e.GetTableName() == "outbox_messages");

        Assert.NotNull(outbox);
        Assert.Equal(KernelDbContext.SchemaName, outbox.GetSchema());
        Assert.True(
            outbox.IsTableExcludedFromMigrations(),
            "A module maps the outbox to write into it, but must not own its schema.");
    }

    private static IModel BuildDesignTimeModel(string contextName)
    {
        using DbContext context = CreateContext(contextName);

        return context.GetService<IDesignTimeModel>().Model;
    }

    private static IModel BuildModel(string contextName) => CreateContext(contextName).Model;

    private static DbContext CreateContext(string contextName) => contextName switch
    {
        nameof(KernelDbContext) => new KernelDbContext(
            new DbContextOptionsBuilder<KernelDbContext>()
                .UseNpgsql("Host=localhost;Database=model_only")
                .Options),

        nameof(IdentityDbContext) => new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql("Host=localhost;Database=model_only")
                .Options),

        nameof(OrganizationDbContext) => new OrganizationDbContext(
            new DbContextOptionsBuilder<OrganizationDbContext>()
                .UseNpgsql("Host=localhost;Database=model_only")
                .Options),

        nameof(AuthorizationDbContext) => new AuthorizationDbContext(
            new DbContextOptionsBuilder<AuthorizationDbContext>()
                .UseNpgsql("Host=localhost;Database=model_only")
                .Options),

        _ => throw new ArgumentOutOfRangeException(nameof(contextName), contextName, "Unknown context.")
    };
}
