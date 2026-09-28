using CCP.Kernel.Results;
using CCP.Modules.Organization.Application.Abstractions;
using CCP.Modules.Organization.Application.Employees;
using CCP.Modules.Organization.Contracts.Dtos;
using CCP.Modules.Organization.Domain;
using CCP.Modules.Organization.Domain.Companies;
using CCP.Modules.Organization.Domain.Employees;
using CCP.Modules.Organization.Domain.Units;
using CCP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CCP.Api.IntegrationTests.Organization;

/// <summary>
/// Reading one employee, and reading the employee behind an account.
/// <para>
/// <b>Both were missing, and the first business application to integrate found
/// out.</b> The Platform could list employees and could not fetch one, so an
/// application that had stored an employee identifier — which is what every
/// application that assigns anything to a person does — had to page through a
/// search to find them again. And <c>EmployeeDto.userId</c> existed as a field
/// nothing could search by, which left an application holding the user id that
/// authentication returns with no way across to the person.
/// </para>
/// <para>
/// Tested against a database rather than a stub, because what is being checked
/// is partly that the unit and position codes come back resolved: a caller
/// showing an employee should need one request, and a handler that returned
/// empty codes would look correct in every assertion that only reads the name.
/// </para>
/// </summary>
public sealed class EmployeeLookupTests(PlatformApiFactory factory)
    : IClassFixture<PlatformApiFactory>
{
    [Fact]
    public async Task AnEmployeeCanBeReadByTheirOwnIdentifier()
    {
        (Guid companyId, Guid unitId, string unitCode) = await AUnitAsync();

        (Guid employeeId, _) = await AnEmployeeAsync(companyId, unitId);

        Result<EmployeeDto> result = await ReadAsync(repository =>
            new GetEmployeeHandler(repository).HandleAsync(new GetEmployeeQuery(employeeId)));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(employeeId, result.Value.Id);

        // Resolved, not blank. This is the half a stub would never catch.
        Assert.Equal(unitCode, result.Value.UnitCode);
    }

    [Fact]
    public async Task AnIdentifierThatBelongsToNobodyIsNotFound()
    {
        Result<EmployeeDto> result = await ReadAsync(repository =>
            new GetEmployeeHandler(repository).HandleAsync(new GetEmployeeQuery(Guid.CreateVersion7())));

        Assert.True(result.IsFailure);
        Assert.Equal("ORGANIZATION.EMPLOYEE_NOT_FOUND", result.Error.Code);
    }

    /// <summary>
    /// The bridge an application crosses after somebody signs in: authentication
    /// answers with a user, and everything done with a person needs the employee.
    /// </summary>
    [Fact]
    public async Task TheEmployeeBehindAnAccountCanBeFound()
    {
        (Guid companyId, Guid unitId, _) = await AUnitAsync();

        (Guid employeeId, Guid userId) = await AnEmployeeAsync(companyId, unitId);

        Result<EmployeeDto> result = await ReadAsync(repository =>
            new GetEmployeeByUserHandler(repository).HandleAsync(new GetEmployeeByUserQuery(userId)));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(employeeId, result.Value.Id);
        Assert.Equal(userId, result.Value.UserId);
    }

    /// <summary>
    /// An account with nobody behind it is a real answer, not an error in the
    /// Platform. Contractors, service accounts and the bootstrap administrator
    /// all exist without an employee record, and an application has to be able
    /// to tell that apart from a failure.
    /// </summary>
    [Fact]
    public async Task AnAccountWithNoEmployeeIsNotFoundRatherThanAFailure()
    {
        Result<EmployeeDto> result = await ReadAsync(repository =>
            new GetEmployeeByUserHandler(repository)
                .HandleAsync(new GetEmployeeByUserQuery(Guid.CreateVersion7())));

        Assert.True(result.IsFailure);
        Assert.Equal("ORGANIZATION.EMPLOYEE_NOT_FOUND", result.Error.Code);
    }

    /// <summary>
    /// An employee who was never given an account is not reachable by one, and
    /// must not be reached by somebody else's.
    /// </summary>
    [Fact]
    public async Task AnEmployeeWithNoAccountIsReachableOnlyByTheirOwnIdentifier()
    {
        (Guid companyId, Guid unitId, _) = await AUnitAsync();

        (Guid employeeId, _) = await AnEmployeeAsync(companyId, unitId, withAccount: false);

        Result<EmployeeDto> byId = await ReadAsync(repository =>
            new GetEmployeeHandler(repository).HandleAsync(new GetEmployeeQuery(employeeId)));

        Assert.True(byId.IsSuccess);
        Assert.Null(byId.Value.UserId);
    }

    // --- Fixtures -----------------------------------------------------------

    private OrganizationDbContext Organization()
    {
        // Touching Services first is what runs the migrations; the connection
        // string on its own gives a database with no schema in it.
        _ = factory.Services;

        return new OrganizationDbContext(
            new DbContextOptionsBuilder<OrganizationDbContext>()
                .UseNpgsql(factory.TestConnectionString)
                .Options);
    }

    private async Task<Result<EmployeeDto>> ReadAsync(
        Func<IOrganizationRepository, Task<Result<EmployeeDto>>> read)
    {
        await using OrganizationDbContext context = Organization();

        return await read(new OrganizationRepository(context));
    }

    /// <summary>
    /// Version 4, not 7: the first characters of a UUIDv7 are a timestamp, so
    /// truncating one produces codes that collide for everything created in the
    /// same few milliseconds — which is what a test method does.
    /// </summary>
    private static string ACode(char prefix) => $"{prefix}{Guid.NewGuid():N}"[..12].ToUpperInvariant();

    private static LocalizedName Named(string english) =>
        LocalizedName.Create($"وحدة {english}", english).Value;

    private async Task<(Guid CompanyId, Guid UnitId, string UnitCode)> AUnitAsync()
    {
        await using OrganizationDbContext context = Organization();

        DateTimeOffset now = DateTimeOffset.UtcNow;

        string companyCode = ACode('C');

        Company company = Company.Create(companyCode, Named(companyCode), "ar", now).Value;

        context.Companies.Add(company);

        string unitCode = ACode('U');

        OrganizationUnit unit = OrganizationUnit.Create(
            company.Id, null, OrganizationUnitType.Department, unitCode, Named(unitCode), now).Value;

        context.Units.Add(unit);

        await context.SaveChangesAsync();

        return (company.Id, unit.Id, unitCode);
    }

    private async Task<(Guid EmployeeId, Guid UserId)> AnEmployeeAsync(
        Guid companyId, Guid unitId, bool withAccount = true)
    {
        await using OrganizationDbContext context = Organization();

        DateTimeOffset now = DateTimeOffset.UtcNow;

        Employee employee = Employee.Create(
            companyId, ACode('E'), Named("Person"), unitId, null, null, now).Value;

        Guid userId = Guid.CreateVersion7();

        if (withAccount)
        {
            employee.LinkUser(userId, now);
        }

        context.Employees.Add(employee);

        await context.SaveChangesAsync();

        return (employee.Id, userId);
    }
}
