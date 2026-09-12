using CCP.Modules.Organization.Contracts;
using CCP.Modules.Organization.Domain;
using CCP.Modules.Organization.Domain.Companies;
using CCP.Modules.Organization.Domain.Employees;
using CCP.Modules.Organization.Domain.Units;
using CCP.Modules.Organization.Infrastructure;
using CCP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CCP.Api.IntegrationTests.Organization;

/// <summary>
/// Which accounts belong to the people under a unit.
/// <para>
/// <b>The query that finally gives a scope a meaning on the user list</b> (debt
/// #15). A caller with <c>platform.users.view</c> at unit scope saw every
/// account in the company, because Identity has no organizational dimension of
/// its own and nobody had decided what a department-scoped list of accounts
/// should contain.
/// </para>
/// <para>
/// The decision is that an account has no department but the person behind it
/// does, which makes this a prefix scan on the materialized path — and puts the
/// whole correctness of the scope in one join that only a database can check.
/// Each case below is a row that exists and must not be counted, and every one
/// of them fails in the direction of showing somebody too much.
/// </para>
/// </summary>
public sealed class UserPlacementTests(PlatformApiFactory factory)
    : IClassFixture<PlatformApiFactory>
{
    [Fact]
    public async Task SomebodyInTheUnitIsUnderIt()
    {
        (Guid companyId, Guid _, string path) = await AUnitAsync();

        Guid userId = await AnEmployeeAsync(companyId, path);

        Assert.Contains(userId, await UnderAsync(path));
    }

    /// <summary>
    /// And so is somebody in a unit below it, which is the point of matching on
    /// a path prefix rather than on a unit id.
    /// </summary>
    [Fact]
    public async Task SomebodyInAUnitBelowItIsUnderItToo()
    {
        (Guid companyId, Guid parentId, string parentPath) = await AUnitAsync();
        (_, _, string childPath) = await AUnitAsync(companyId, parentId);

        Guid userId = await AnEmployeeAsync(companyId, childPath);

        Assert.Contains(userId, await UnderAsync(parentPath));
    }

    /// <summary>
    /// Somebody in a sibling department is not. The dull half, and the one a
    /// prefix written without its trailing separator gets wrong: a department
    /// whose path is a prefix of another department's would swallow it.
    /// </summary>
    [Fact]
    public async Task SomebodyInAnotherUnitIsNot()
    {
        (Guid companyId, _, string path) = await AUnitAsync();
        (_, _, string elsewhere) = await AUnitAsync(companyId);

        Guid userId = await AnEmployeeAsync(companyId, elsewhere);

        Assert.DoesNotContain(userId, await UnderAsync(path));
    }

    /// <summary>
    /// An employee who has left is not in the department any more. Their record
    /// survives, because deactivating is not deleting, and a query that forgot
    /// to filter on it would go on showing a former colleague's account for
    /// ever.
    /// </summary>
    [Fact]
    public async Task SomebodyWhoHasLeftIsNot()
    {
        (Guid companyId, _, string path) = await AUnitAsync();

        Guid userId = await AnEmployeeAsync(companyId, path, active: false);

        Assert.DoesNotContain(userId, await UnderAsync(path));
    }

    /// <summary>
    /// An employee with no account contributes nothing, rather than a null.
    /// </summary>
    [Fact]
    public async Task AnEmployeeWithNoAccountContributesNothing()
    {
        (Guid companyId, _, string path) = await AUnitAsync();

        await AnEmployeeAsync(companyId, path, withAccount: false);

        Assert.Empty(await UnderAsync(path));
    }

    /// <summary>
    /// No prefixes means nobody, not everybody.
    /// <para>
    /// <b>The assertion worth having most.</b> A caller whose scope resolved to
    /// no units at all is a caller who may see nothing, and answering "everybody"
    /// there turns a scope that grants nothing into a scope that grants the whole
    /// company — silently, and in the direction nobody notices.
    /// </para>
    /// </summary>
    [Fact]
    public async Task NoPrefixesMeansNobody()
    {
        (Guid companyId, _, string path) = await AUnitAsync();

        await AnEmployeeAsync(companyId, path);

        Assert.Empty(await UnderAsync());
    }

    // --- Fixtures -----------------------------------------------------------

    /// <summary>
    /// Reads the database the host migrated.
    /// <para>
    /// Touching <c>Services</c> first is not decoration: the connection string
    /// is a plain property, so a test that only reads it gets a database with no
    /// schema in it. The host is what runs the migrations.
    /// </para>
    /// </summary>
    private OrganizationDbContext Organization()
    {
        _ = factory.Services;

        return new OrganizationDbContext(
            new DbContextOptionsBuilder<OrganizationDbContext>()
                .UseNpgsql(factory.TestConnectionString)
                .Options);
    }

    private async Task<IReadOnlyList<Guid>> UnderAsync(params string[] prefixes)
    {
        await using OrganizationDbContext context = Organization();

        return await new OrganizationDirectory(context).GetUserIdsUnderAsync(prefixes);
    }

    /// <summary>
    /// Version 4, not 7: the first hex characters of a UUIDv7 are a millisecond
    /// timestamp, so truncating one produces codes that collide for everything
    /// created in the same few milliseconds — which is what a test method does.
    /// </summary>
    private static string ACode(char prefix) => $"{prefix}{Guid.NewGuid():N}"[..12].ToUpperInvariant();

    private static LocalizedName Named(string english) =>
        LocalizedName.Create($"وحدة {english}", english).Value;

    private async Task<(Guid CompanyId, Guid UnitId, string Path)> AUnitAsync(
        Guid? existingCompanyId = null, Guid? parentId = null)
    {
        await using OrganizationDbContext context = Organization();

        Guid companyId;

        if (existingCompanyId is { } given)
        {
            companyId = given;
        }
        else
        {
            string companyCode = ACode('C');

            Company company = Company.Create(
                companyCode, Named(companyCode), "ar", DateTimeOffset.UtcNow).Value;

            context.Companies.Add(company);

            companyId = company.Id;
        }

        OrganizationUnit? parent = parentId is null
            ? null
            : await context.Units.FirstAsync(u => u.Id == parentId);

        string code = ACode('U');

        OrganizationUnit unit = OrganizationUnit.Create(
            companyId, parent, OrganizationUnitType.Department, code,
            Named(code), DateTimeOffset.UtcNow).Value;

        context.Units.Add(unit);

        await context.SaveChangesAsync();

        return (companyId, unit.Id, unit.Path);
    }

    private async Task<Guid> AnEmployeeAsync(
        Guid companyId, string unitPath, bool active = true, bool withAccount = true)
    {
        await using OrganizationDbContext context = Organization();

        OrganizationUnit unit = await context.Units.FirstAsync(u => u.Path == unitPath);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        Employee employee = Employee.Create(
            companyId, ACode('E'), Named("Person"), unit.Id, null, null, now).Value;

        Guid userId = Guid.CreateVersion7();

        if (withAccount)
        {
            employee.LinkUser(userId, now);
        }

        if (!active)
        {
            employee.Deactivate(now);
        }

        context.Employees.Add(employee);

        await context.SaveChangesAsync();

        return userId;
    }
}
