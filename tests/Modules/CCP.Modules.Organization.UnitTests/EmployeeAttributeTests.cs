using CCP.Kernel.Results;
using CCP.Modules.Organization.Domain;
using CCP.Modules.Organization.Domain.Employees;

namespace CCP.Modules.Organization.UnitTests;

/// <summary>
/// The typed extension bag ARCHITECTURE.md §7.2.2 asks for.
/// <para>
/// <b>The word doing the work is "typed".</b> A free-form JSON column would let
/// a business application store anything at all on an employee record —
/// including a credential, and including a field nobody else can interpret — and
/// the Platform would have no answer to "what do you hold about this person",
/// which is a question a company is obliged to answer in full.
/// </para>
/// </summary>
public sealed class EmployeeAttributeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AnAttributeIsKeptUnderItsKey()
    {
        Employee employee = AnEmployee();

        Assert.True(employee.SetAttribute("payroll.cost-centre", "CC-1180", Now).IsSuccess);

        EmployeeAttribute attribute = Assert.Single(employee.Attributes);

        Assert.Equal("payroll.cost-centre", attribute.Key);
        Assert.Equal("CC-1180", attribute.Value);
    }

    [Fact]
    public void SettingTheSameKeyTwiceReplacesTheValue()
    {
        Employee employee = AnEmployee();

        employee.SetAttribute("payroll.cost-centre", "CC-1180", Now);
        employee.SetAttribute("payroll.cost-centre", "CC-2200", Now.AddDays(1));

        EmployeeAttribute attribute = Assert.Single(employee.Attributes);

        Assert.Equal("CC-2200", attribute.Value);
        Assert.Equal(Now.AddDays(1), attribute.SetAt);
    }

    /// <summary>
    /// The key is lower-cased, so <c>Payroll.Status</c> and
    /// <c>payroll.status</c> are the same attribute rather than two that shadow
    /// each other.
    /// </summary>
    [Fact]
    public void KeysAreCaseInsensitive()
    {
        Employee employee = AnEmployee();

        employee.SetAttribute("Payroll.Status", "active", Now);
        employee.SetAttribute("payroll.STATUS", "left", Now);

        EmployeeAttribute attribute = Assert.Single(employee.Attributes);

        Assert.Equal("payroll.status", attribute.Key);
        Assert.Equal("left", attribute.Value);
    }

    /// <summary>
    /// A key with no namespace belongs to nobody, and the first collision would
    /// be silent: one application overwriting another's value on the same
    /// employee, with both convinced they owned it.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("status")]
    [InlineData("payroll.")]
    [InlineData(".status")]
    [InlineData("payroll.status.extra")]
    [InlineData("payroll.sta tus")]
    [InlineData("payroll.status!")]
    public void AKeyThatNamesNoApplicationIsRefused(string key)
        => Assert.True(AnEmployee().SetAttribute(key, "value", Now).IsFailure);

    /// <summary>
    /// Nothing is expressed by removing the attribute, not by storing an empty
    /// string. Two ways of saying "no value" is two things to check everywhere
    /// afterwards.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyValueIsRefused(string value)
        => Assert.True(AnEmployee().SetAttribute("payroll.status", value, Now).IsFailure);

    /// <summary>
    /// <b>The assertion this class exists for.</b>
    /// <para>
    /// An employee record is exported, backed up and broadly readable inside the
    /// company. A credential put here is a credential in all of those places,
    /// and the person who put it there did so because it was convenient — which
    /// is exactly when it happens.
    /// </para>
    /// <para>
    /// The guard is the Configuration module's, now in the kernel, because the
    /// argument was never about settings and a second copy of a security
    /// heuristic is a second copy that drifts.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----")]
    [InlineData("sk-abcdefghijklmnopqrstuvwxyz012345")]
    [InlineData("aB3xK9mQ7vT2wR4zY8nP5jL1hG6dF0sC")]
    public void ACredentialPastedWhereMetadataBelongsIsRefused(string value)
    {
        Result set = AnEmployee().SetAttribute("payroll.token", value, Now);

        Assert.True(set.IsFailure);
        Assert.Equal(
            OrganizationErrors.AttributeLooksLikeASecret.Code, set.Errors[0].Code);
    }

    /// <summary>
    /// A bag with no limit is a table somebody eventually uses as a database,
    /// and the row it lives on is one a company has to be able to describe in
    /// full when somebody asks what is held about them.
    /// </summary>
    [Fact]
    public void ThereIsALimit()
    {
        Employee employee = AnEmployee();

        for (int i = 0; i < Employee.MaximumAttributes; i++)
        {
            Assert.True(
                employee.SetAttribute($"payroll.field-{i}", "value", Now).IsSuccess);
        }

        Assert.True(employee.SetAttribute("payroll.one-too-many", "value", Now).IsFailure);

        // And replacing an existing one still works at the limit, because that
        // adds nothing. A limit that blocked edits would strand an application
        // with fifty attributes it could never correct.
        Assert.True(employee.SetAttribute("payroll.field-0", "corrected", Now).IsSuccess);
    }

    /// <summary>
    /// Removing is idempotent: an application clearing its own attributes should
    /// not have to ask first, and "it was already gone" is the outcome it
    /// wanted.
    /// </summary>
    [Fact]
    public void RemovingWhatIsNotThereIsFine()
    {
        Employee employee = AnEmployee();

        employee.SetAttribute("payroll.status", "active", Now);

        employee.RemoveAttribute("payroll.status", Now);
        employee.RemoveAttribute("payroll.status", Now);
        employee.RemoveAttribute("payroll.never-set", Now);

        Assert.Empty(employee.Attributes);
    }

    /// <summary>
    /// Two applications keep their own attributes side by side. This is the
    /// whole point of the namespace, and the case a single flat key space gets
    /// silently wrong.
    /// </summary>
    [Fact]
    public void TwoApplicationsDoNotCollide()
    {
        Employee employee = AnEmployee();

        employee.SetAttribute("payroll.status", "active", Now);
        employee.SetAttribute("fleet.status", "assigned", Now);

        Assert.Equal(2, employee.Attributes.Count);
    }

    private static Employee AnEmployee()
        => Employee.Create(
            Guid.CreateVersion7(),
            "E-0001",
            LocalizedName.Create("موظف", "Employee").Value,
            Guid.CreateVersion7(),
            null,
            null,
            Now).Value;
}
