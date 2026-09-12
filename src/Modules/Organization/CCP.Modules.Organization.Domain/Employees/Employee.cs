using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Organization.Domain.Units.Events;

namespace CCP.Modules.Organization.Domain.Employees;

/// <summary>
/// A person's place in the organization.
/// <para>
/// <b>This is the sharpest boundary in the Platform</b> (ADR-005 §4.3a). An
/// employee record answers "who works here, in which unit, reporting to whom" —
/// an organizational fact every system needs. It does <b>not</b> answer what
/// they are paid, what leave they have taken, how they were appraised or what
/// their contract says. Those are HR business data, and they live in the HR
/// application, which references this record by id.
/// </para>
/// <para>
/// The pressure to add "just one field" here will be constant and will always
/// sound reasonable. Salary is the canonical example: it is obviously about an
/// employee, and admitting it would put payroll rules in the Platform within a
/// year. The test in ARCHITECTURE.md §4.4 applies to every field proposed for
/// this type.
/// </para>
/// <para>
/// An employee may exist without a Platform user account (a worker who needs no
/// system access), and a user may exist without an employee record (a service
/// account, a contractor). The link is optional in both directions, and that is
/// deliberate.
/// </para>
/// </summary>
public sealed class Employee : AggregateRoot, IAuditableEntity
{
    /// <summary>
    /// How many custom attributes one employee may carry.
    /// <para>
    /// A bag with no limit is a table somebody eventually uses as a database,
    /// and the row it lives on is one a company has to be able to describe in
    /// full when somebody asks what is held about them.
    /// </para>
    /// </summary>
    public const int MaximumAttributes = 50;

    private readonly List<EmployeeAttribute> _attributes = [];

    private Employee() { }

    private Employee(
        Guid id,
        Guid companyId,
        string employeeNumber,
        LocalizedName fullName,
        Guid unitId,
        Guid? positionId,
        DateTimeOffset now)
        : base(id)
    {
        CompanyId = companyId;
        EmployeeNumber = employeeNumber;
        FullName = fullName;
        UnitId = unitId;
        PositionId = positionId;
        IsActive = true;
        CreatedAt = now;
    }

    public Guid CompanyId { get; private set; }

    /// <summary>
    /// The company's own identifier for this person — the number on their badge.
    /// Unique within the company, and the key people actually use.
    /// </summary>
    public string EmployeeNumber { get; private set; } = string.Empty;

    public LocalizedName FullName { get; private set; } = null!;

    /// <summary>
    /// The Platform account, when the person has one.
    /// <para>
    /// Stored as a plain id with <b>no foreign key</b> — Identity is a different
    /// module and a different schema, and a cross-schema key would make both
    /// unextractable (ADR-004 §10.2). Integrity is enforced by the use case,
    /// which verifies the user exists through Identity's contract.
    /// </para>
    /// </summary>
    public Guid? UserId { get; private set; }

    /// <summary>
    /// What business applications keep about this person (§7.2.2).
    /// <para>
    /// <b>Typed and namespaced, not a JSON column.</b> A free-form bag would let
    /// an application store anything at all on an employee record — including a
    /// credential, and including a field nobody else can interpret — and the
    /// Platform would have no answer to "what do you hold about this person",
    /// which is a question a company is obliged to answer.
    /// </para>
    /// </summary>
    public IReadOnlyList<EmployeeAttribute> Attributes => _attributes;

    /// <summary>The unit this person belongs to.</summary>
    public Guid UnitId { get; private set; }

    public Guid? PositionId { get; private set; }

    /// <summary>
    /// Who this person reports to. Null for the top of the chain.
    /// <para>
    /// A single manager, not a matrix. Workflow resolves "the requester's
    /// manager" through this (ARCHITECTURE.md §16.3), and that resolution has to
    /// be unambiguous. Dotted-line reporting would need its own entity, and
    /// nobody has asked for it (P1).
    /// </para>
    /// </summary>
    public Guid? ManagerId { get; private set; }

    /// <summary>
    /// A work email for directory purposes. Not a credential — authentication
    /// uses the Identity account's email, which may differ.
    /// </summary>
    public string? WorkEmail { get; private set; }

    public string? WorkPhone { get; private set; }

    public DateOnly? HireDate { get; private set; }

    /// <summary>
    /// Employees are deactivated, not deleted. Business records across every
    /// company system refer to them, and audit entries must stay readable years
    /// later.
    /// </summary>
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    // -----------------------------------------------------------------------
    // Creation
    // -----------------------------------------------------------------------

    public static Result<Employee> Create(
        Guid companyId,
        string employeeNumber,
        LocalizedName fullName,
        Guid unitId,
        Guid? positionId,
        DateOnly? hireDate,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(employeeNumber))
        {
            return Result.Failure<Employee>(OrganizationErrors.EmployeeNumberRequired);
        }

        var employee = new Employee(
            Uuid7.NewGuid(now),
            companyId,
            employeeNumber.Trim(),
            fullName,
            unitId,
            positionId,
            now)
        {
            HireDate = hireDate
        };

        employee.Raise(new EmployeeHiredEvent(
            employee.Id, employee.EmployeeNumber, employee.FullName.English, unitId, null, now));

        return employee;
    }

    // -----------------------------------------------------------------------
    // Organizational changes
    // -----------------------------------------------------------------------

    /// <summary>
    /// Moves the employee to a different unit, position or both.
    /// <para>
    /// Raises <see cref="EmployeeTransferredEvent"/>, which consumers must treat
    /// as invalidating any cached organizational scope for this person: from
    /// Phase 4, a <c>Unit</c> grant follows the employee record, so a transfer
    /// changes what they can see.
    /// </para>
    /// </summary>
    public Result Transfer(Guid newUnitId, Guid? newPositionId, DateTimeOffset now)
    {
        if (newUnitId == UnitId && newPositionId == PositionId)
        {
            return Result.Success();
        }

        Guid previousUnitId = UnitId;

        UnitId = newUnitId;
        PositionId = newPositionId;
        UpdatedAt = now;

        Raise(new EmployeeTransferredEvent(
            Id, EmployeeNumber, previousUnitId, newUnitId, UserId, now));

        return Result.Success();
    }

    /// <summary>
    /// Sets the reporting line.
    /// <para>
    /// The caller must have established that <paramref name="managerId"/> does
    /// not report to this employee — this type cannot walk the chain, because it
    /// holds only one link of it. See <see cref="ManagementChain"/>.
    /// </para>
    /// </summary>
    public Result AssignManager(Guid? managerId, DateTimeOffset now)
    {
        if (managerId == Id)
        {
            return Result.Failure(OrganizationErrors.EmployeeCannotManageThemselves);
        }

        if (managerId == ManagerId)
        {
            return Result.Success();
        }

        Guid? previousManagerId = ManagerId;

        ManagerId = managerId;
        UpdatedAt = now;

        Raise(new ManagerChangedEvent(Id, EmployeeNumber, previousManagerId, managerId, now));

        return Result.Success();
    }

    /// <summary>
    /// Links or unlinks the Platform account.
    /// <para>
    /// The caller must have verified that the account exists and is not already
    /// linked to someone else — both are facts this module cannot see, since
    /// Identity is a different module.
    /// </para>
    /// </summary>
    public Result LinkUser(Guid? userId, DateTimeOffset now)
    {
        if (userId == UserId)
        {
            return Result.Success();
        }

        Guid? previousUserId = UserId;

        UserId = userId;
        UpdatedAt = now;

        Raise(new EmployeeUserLinkChangedEvent(Id, EmployeeNumber, previousUserId, userId, now));

        return Result.Success();
    }

    public Result UpdateContactDetails(string? workEmail, string? workPhone, DateTimeOffset now)
    {
        WorkEmail = string.IsNullOrWhiteSpace(workEmail) ? null : workEmail.Trim().ToLowerInvariant();
        WorkPhone = string.IsNullOrWhiteSpace(workPhone) ? null : workPhone.Trim();
        UpdatedAt = now;

        return Result.Success();
    }

    public Result Rename(LocalizedName fullName, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(fullName);

        FullName = fullName;
        UpdatedAt = now;

        return Result.Success();
    }

    public Result Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return Result.Failure(OrganizationErrors.EmployeeAlreadyInactive);
        }

        IsActive = false;
        UpdatedAt = now;

        Raise(new EmployeeDeactivatedEvent(Id, EmployeeNumber, UserId, now));

        return Result.Success();
    }

    public Result Reactivate(DateTimeOffset now)
    {
        if (IsActive)
        {
            return Result.Failure(OrganizationErrors.EmployeeAlreadyActive);
        }

        IsActive = true;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Sets one custom attribute, or replaces it.
    /// <para>
    /// No event is raised. An attribute is an application's own note about
    /// somebody, not an organizational fact — raising <c>EmployeeChanged</c> for
    /// one would wake every subscriber for something none of them can interpret.
    /// </para>
    /// </summary>
    public Result SetAttribute(string key, string value, DateTimeOffset now)
    {
        Result<string> name = EmployeeAttribute.NormaliseKey(key);

        if (name.IsFailure)
        {
            return Result.Failure(name.Errors);
        }

        EmployeeAttribute? existing = Find(name.Value);

        if (existing is not null)
        {
            Result updated = existing.Update(value, now);

            if (updated.IsSuccess)
            {
                UpdatedAt = now;
            }

            return updated;
        }

        // Counted before it is added, so the limit is a limit rather than a
        // number the fiftieth write happens to pass.
        if (_attributes.Count >= MaximumAttributes)
        {
            return Result.Failure(OrganizationErrors.AttributeLimitReached);
        }

        Result<EmployeeAttribute> created = EmployeeAttribute.Set(Id, name.Value, value, now);

        if (created.IsFailure)
        {
            return Result.Failure(created.Errors);
        }

        _attributes.Add(created.Value);
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Removes one, and says nothing when there was nothing to remove.
    /// <para>
    /// Idempotent because an application clearing its own attributes should not
    /// have to ask first, and "it was already gone" is the outcome it wanted.
    /// </para>
    /// </summary>
    public void RemoveAttribute(string key, DateTimeOffset now)
    {
        Result<string> name = EmployeeAttribute.NormaliseKey(key);

        if (name.IsFailure)
        {
            return;
        }

        EmployeeAttribute? existing = Find(name.Value);

        if (existing is null)
        {
            return;
        }

        _attributes.Remove(existing);
        UpdatedAt = now;
    }

    private EmployeeAttribute? Find(string key)
        => _attributes.FirstOrDefault(
            attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal));
}
