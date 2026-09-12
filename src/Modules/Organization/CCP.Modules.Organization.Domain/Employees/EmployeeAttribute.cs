using CCP.Kernel.Domain;
using CCP.Kernel.Results;
using CCP.Kernel.Security;

namespace CCP.Modules.Organization.Domain.Employees;

/// <summary>
/// One piece of metadata a business application keeps about an employee.
/// <para>
/// <b>ARCHITECTURE.md §7.2.2 asks for a typed extension bag, and the word doing
/// the work is "typed".</b> A free-form JSON column would let a business
/// application store anything at all on an employee record — including a
/// credential, and including a field nobody else can interpret — and the Platform
/// would have no answer to "what do you hold about this person", which is a
/// question a company is legally obliged to answer.
/// </para>
/// <para>
/// So an attribute is declared before it is set, in its owner's namespace, the
/// same way a setting is. The declaration is what makes the bag readable by
/// somebody who did not write the application that filled it.
/// </para>
/// </summary>
public sealed class EmployeeAttribute : Entity
{
    private EmployeeAttribute() { }

    private EmployeeAttribute(
        Guid id, Guid employeeId, string key, string value, DateTimeOffset now)
        : base(id)
    {
        EmployeeId = employeeId;
        Key = key;
        Value = value;
        SetAt = now;
    }

    public Guid EmployeeId { get; private set; }

    /// <summary>
    /// The attribute's full name: <c>&lt;application&gt;.&lt;name&gt;</c>.
    /// <para>
    /// Namespaced, so two business systems that both care about "status" do not
    /// collide — and so an attribute's owner can be read off the key rather than
    /// looked up.
    /// </para>
    /// </summary>
    public string Key { get; private set; } = string.Empty;

    public string Value { get; private set; } = string.Empty;

    public DateTimeOffset SetAt { get; private set; }

    internal static Result<EmployeeAttribute> Set(
        Guid employeeId, string key, string value, DateTimeOffset now)
    {
        Result<string> name = NormaliseKey(key);

        if (name.IsFailure)
        {
            return Result.Failure<EmployeeAttribute>(name.Errors);
        }

        Result checkedValue = CheckValue(value);

        if (checkedValue.IsFailure)
        {
            return Result.Failure<EmployeeAttribute>(checkedValue.Errors);
        }

        return Result.Success(
            new EmployeeAttribute(Guid.CreateVersion7(), employeeId, name.Value, value.Trim(), now));
    }

    internal Result Update(string value, DateTimeOffset now)
    {
        Result checkedValue = CheckValue(value);

        if (checkedValue.IsFailure)
        {
            return checkedValue;
        }

        Value = value.Trim();
        SetAt = now;

        return Result.Success();
    }

    /// <summary>
    /// The shape a key must have: an application namespace and a name, nothing
    /// else.
    /// <para>
    /// A key with no namespace belongs to nobody, and the first collision would
    /// be silent — one application overwriting another's value on the same
    /// employee, with both convinced they owned it.
    /// </para>
    /// </summary>
    internal static Result<string> NormaliseKey(string key)
    {
        string trimmed = (key ?? string.Empty).Trim().ToLowerInvariant();

        if (trimmed.Length is 0 or > 100)
        {
            return Result.Failure<string>(OrganizationErrors.AttributeKeyInvalid);
        }

        string[] parts = trimmed.Split('.');

        if (parts.Length != 2 || parts.Any(part => part.Length == 0))
        {
            return Result.Failure<string>(OrganizationErrors.AttributeKeyInvalid);
        }

        return parts.All(part => part.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            ? Result.Success(trimmed)
            : Result.Failure<string>(OrganizationErrors.AttributeKeyInvalid);
    }

    private static Result CheckValue(string value)
    {
        if (value is null || value.Trim().Length == 0)
        {
            // Nothing is expressed by removing the attribute, not by storing an
            // empty string. Two ways to say "no value" is two things to check
            // everywhere afterwards.
            return Result.Failure(OrganizationErrors.AttributeValueRequired);
        }

        if (value.Length > 1000)
        {
            return Result.Failure(OrganizationErrors.AttributeValueTooLong);
        }

        // The same guard the Configuration module applies to a setting, for the
        // same reason and now from the same place. An employee record is
        // exported, backed up and broadly readable inside the company; a
        // credential put here is a credential in all of those.
        return SecretShapedValue.Looks(value)
            ? Result.Failure(OrganizationErrors.AttributeLooksLikeASecret)
            : Result.Success();
    }
}
