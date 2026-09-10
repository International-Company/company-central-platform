using CCP.Kernel.Results;
using CCP.Modules.Organization.Application.Abstractions;
using CCP.Modules.Organization.Domain;
using CCP.Modules.Organization.Domain.Companies;
using CCP.Modules.Organization.Domain.Units;
using CCP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CCP.Api.IntegrationTests.Organization;

/// <summary>
/// The half of the hierarchy that only a database can answer.
/// <para>
/// <b>Forty-eight unit tests cover the logic and none of them touch
/// PostgreSQL</b> (debt #12). What they cannot show is whether a move of a
/// subtree really commits atomically, whether the unique constraints exist as
/// constraints rather than as intentions, and whether the <c>text_pattern_ops</c>
/// index the scope filter depends on is actually used — that last one is not a
/// correctness question at all until the day the company has four thousand
/// units, at which point every request that resolves an organizational scope
/// starts doing a sequential scan and nothing anywhere reports an error.
/// </para>
/// </summary>
public sealed class HierarchyPersistenceTests(PlatformApiFactory factory)
    : IClassFixture<PlatformApiFactory>
{
    private static LocalizedName Named(string english) =>
        LocalizedName.Create($"وحدة {english}", english).Value;

    /// <summary>
    /// A move rebases the whole subtree, and the descendants are written in the
    /// same transaction as the unit that moved.
    /// <para>
    /// The paths are what organizational scope is computed from. A move that
    /// committed the unit and left its descendants pointing at the old prefix
    /// would leave people with access to a branch they are no longer under —
    /// which is a permission defect wearing a data-consistency costume.
    /// </para>
    /// </summary>
    [Fact]
    public async Task MovingASubtree_RebasesEveryDescendant()
    {
        Guid companyId = await ACompanyAsync();

        // root ── engineering ── backend ── platform
        //     └── operations
        (Guid rootId, string rootPath) = await AUnitAsync(companyId, null, "ROOT");
        (Guid engineeringId, _) = await AUnitAsync(companyId, rootId, "ENG");
        (Guid backendId, _) = await AUnitAsync(companyId, engineeringId, "BACKEND");
        (Guid platformId, _) = await AUnitAsync(companyId, backendId, "PLATFORM");
        (Guid operationsId, string operationsPath) = await AUnitAsync(companyId, rootId, "OPS");

        await MoveAsync(backendId, operationsId);

        await using OrganizationDbContext context = Organization();

        OrganizationUnit backend = await context.Units.AsNoTracking().SingleAsync(u => u.Id == backendId);
        OrganizationUnit platform = await context.Units.AsNoTracking().SingleAsync(u => u.Id == platformId);

        Assert.StartsWith(operationsPath, backend.Path, StringComparison.Ordinal);
        Assert.Equal(operationsId, backend.ParentId);

        // The grandchild is the one that matters. It was never named in the
        // command, and its path has to have moved with its parent.
        Assert.StartsWith(backend.Path, platform.Path, StringComparison.Ordinal);
        Assert.Equal(backend.Depth + 1, platform.Depth);

        // And it must no longer be under where it came from.
        Assert.DoesNotContain(engineeringId.ToString("N")[..8], platform.Path, StringComparison.Ordinal);

        _ = rootPath;
    }

    /// <summary>
    /// A cycle is refused, and refused before anything is written.
    /// <para>
    /// Moving a unit under its own descendant would produce a subtree unreachable
    /// from the root and a rebase loop with no fixed point. The domain refuses
    /// it; this asserts the database is left exactly as it was, because a
    /// half-applied refusal is worse than the cycle.
    /// </para>
    /// </summary>
    [Fact]
    public async Task MovingAUnitUnderItsOwnDescendant_ChangesNothing()
    {
        Guid companyId = await ACompanyAsync();

        (Guid rootId, _) = await AUnitAsync(companyId, null, "CYCLEROOT");
        (Guid childId, _) = await AUnitAsync(companyId, rootId, "CYCLECHILD");
        (Guid grandchildId, string grandchildPath) = await AUnitAsync(companyId, childId, "CYCLEGRAND");

        string before = await PathOfAsync(childId);

        Result result = await MoveAsync(childId, grandchildId, expectSuccess: false);

        Assert.True(result.IsFailure);
        Assert.Equal(before, await PathOfAsync(childId));
        Assert.Equal(grandchildPath, await PathOfAsync(grandchildId));
    }

    /// <summary>
    /// The unique constraint on (company, code) is enforced by PostgreSQL.
    /// <para>
    /// The handler checks for a duplicate first, which is the right thing for
    /// the error message and is <b>not</b> a guarantee: two requests arriving
    /// together both find nothing and both insert. Only the constraint stops
    /// that, so the constraint is what is tested — by going around the handler
    /// entirely.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TwoUnitsCannotShareACodeWithinACompany()
    {
        Guid companyId = await ACompanyAsync();

        await AUnitAsync(companyId, null, "DUPLICATE");

        await using OrganizationDbContext context = Organization();

        OrganizationUnit clash = OrganizationUnit.Create(
            companyId, null, OrganizationUnitType.Department, "DUPLICATE",
            Named("Clash"), DateTimeOffset.UtcNow).Value;

        context.Units.Add(clash);

        DbUpdateException failure =
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.IsType<PostgresException>(failure.InnerException);
        Assert.Equal("23505", ((PostgresException)failure.InnerException!).SqlState);
    }

    /// <summary>
    /// The same code in a different company is fine.
    /// <para>
    /// Worth asserting, because a unique index on <c>code</c> alone would pass
    /// the test above and quietly make the Platform single-tenant.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheSameCodeInAnotherCompanyIsAllowed()
    {
        Guid firstCompany = await ACompanyAsync();
        Guid secondCompany = await ACompanyAsync();

        await AUnitAsync(firstCompany, null, "SHARED");
        await AUnitAsync(secondCompany, null, "SHARED");

        await using OrganizationDbContext context = Organization();

        Assert.Equal(
            2,
            await context.Units.CountAsync(u => u.Code == "SHARED"));
    }

    /// <summary>
    /// The subtree query uses the path index rather than scanning the table.
    /// <para>
    /// <b>This is the one that cannot be checked by reading the code.</b> The
    /// scope filter runs a <c>LIKE 'prefix%'</c> on every request that resolves
    /// an organizational scope, and B-tree indexes do not serve that pattern
    /// unless the index was built with <c>text_pattern_ops</c> — which the model
    /// asks for and only PostgreSQL can confirm it got. Losing it costs no
    /// correctness and no error message; it costs a sequential scan on the
    /// hottest query in the Platform.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheSubtreeQueryUsesThePathIndex()
    {
        Guid companyId = await ACompanyAsync();
        (Guid rootId, string rootPath) = await AUnitAsync(companyId, null, "PLANROOT");

        for (int i = 0; i < 40; i++)
        {
            await AUnitAsync(companyId, rootId, $"PLAN{i:D3}");
        }

        await using NpgsqlConnection connection = new(factory.TestConnectionString);
        await connection.OpenAsync();

        // The planner will prefer a sequential scan on a small table whatever
        // indexes exist, which would make this assert nothing. Disabling it asks
        // the question actually being asked: *can* this be served by an index?
        await using (NpgsqlCommand off = connection.CreateCommand())
        {
            off.CommandText = "SET enable_seqscan = off;";
            await off.ExecuteNonQueryAsync();
        }

        // The pattern is inlined rather than parameterised, and that is a
        // deliberate limit on what this test claims. PostgreSQL can only turn
        // `LIKE 'prefix%'` into an index range scan when it knows the pattern at
        // plan time -- with a parameter it depends on whether a custom plan is
        // chosen, which is planner behaviour rather than a property of our
        // schema. The question being asked here is narrower and is the one that
        // matters: does an index exist that *can* serve this?
        //
        // Safe to inline: a path contains only hex digits and slashes, asserted
        // immediately below, so there is no metacharacter to escape.
        Assert.Matches("^[0-9a-fA-F/]+$", rootPath);

        await using NpgsqlCommand explain = connection.CreateCommand();
        explain.CommandText =
            $"EXPLAIN (FORMAT TEXT) SELECT id FROM organization.units WHERE path LIKE '{rootPath}%';";

        var plan = new List<string>();

        await using (NpgsqlDataReader reader = await explain.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                plan.Add(reader.GetString(0));
            }
        }

        string text = string.Join('\n', plan);

        Assert.Contains("ix_units_path", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A unit at the top of the tree has no parent and depth zero, and that
    /// survives a round trip through the database.
    /// </summary>
    [Fact]
    public async Task ARootUnitPersistsWithNoParent()
    {
        Guid companyId = await ACompanyAsync();
        (Guid rootId, string path) = await AUnitAsync(companyId, null, "TOP");

        await using OrganizationDbContext context = Organization();

        OrganizationUnit root = await context.Units.AsNoTracking().SingleAsync(u => u.Id == rootId);

        Assert.Null(root.ParentId);
        Assert.Equal(0, root.Depth);
        Assert.Equal(path, root.Path);
        Assert.False(string.IsNullOrWhiteSpace(root.Path));
    }

    // --- Fixtures -----------------------------------------------------------

    private OrganizationDbContext Organization() =>
        new(new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseNpgsql(factory.TestConnectionString)
            .Options);

    private async Task<Guid> ACompanyAsync()
    {
        await using OrganizationDbContext context = Organization();

        string code = $"C{Guid.CreateVersion7():N}"[..12].ToUpperInvariant();

        Company company = Company.Create(
            code, Named(code), "ar", DateTimeOffset.UtcNow).Value;

        context.Companies.Add(company);
        await context.SaveChangesAsync();

        return company.Id;
    }

    /// <summary>
    /// Creates a unit through the repository, so the path is derived the way the
    /// application derives it rather than by the test inventing one.
    /// </summary>
    private async Task<(Guid Id, string Path)> AUnitAsync(
        Guid companyId, Guid? parentId, string code)
    {
        await using OrganizationDbContext context = Organization();

        OrganizationUnit? parent = parentId is null
            ? null
            : await context.Units.FirstAsync(u => u.Id == parentId);

        OrganizationUnit unit = OrganizationUnit.Create(
            companyId, parent, OrganizationUnitType.Department, code,
            Named(code), DateTimeOffset.UtcNow).Value;

        context.Units.Add(unit);
        await context.SaveChangesAsync();

        return (unit.Id, unit.Path);
    }

    /// <summary>
    /// Moves through the real handler, resolved from the running host, so the
    /// transaction boundary under test is the production one.
    /// </summary>
    private async Task<Result> MoveAsync(Guid unitId, Guid? newParentId, bool expectSuccess = true)
    {
        using IServiceScope scope = factory.Services.CreateScope();

        var handler = scope.ServiceProvider
            .GetRequiredService<Modules.Organization.Application.Units.MoveUnitHandler>();

        Result result = await handler.HandleAsync(
            new Modules.Organization.Application.Units.MoveUnitCommand(unitId, newParentId));

        if (expectSuccess)
        {
            Assert.True(result.IsSuccess, $"The move failed: {FirstCode(result)}");
        }

        return result;
    }

    private static string FirstCode(Result result) =>
        result.Errors.Count > 0 ? result.Errors[0].Code : "(none)";

    private async Task<string> PathOfAsync(Guid unitId)
    {
        await using OrganizationDbContext context = Organization();

        return (await context.Units.AsNoTracking().SingleAsync(u => u.Id == unitId)).Path;
    }
}
