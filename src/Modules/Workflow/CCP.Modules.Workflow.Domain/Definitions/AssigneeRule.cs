using CCP.Kernel.Results;

namespace CCP.Modules.Workflow.Domain.Definitions;

/// <summary>
/// How a step decides who has to act.
/// <para>
/// <b>Organizational, never business-conditional</b> (ARCHITECTURE.md §16.3).
/// Every strategy here answers "which person, by their place in the company" —
/// a named user, whoever holds a role, whoever holds a position, the requester's
/// manager, the head of a unit, or a list the calling application worked out for
/// itself.
/// </para>
/// <para>
/// Note what is absent: there is no "if the amount exceeds X, route to the
/// finance director". Routing that depends on business data is decided by the
/// application, which either supplies the assignees at start time or answers a
/// callback. The engine holds no thresholds, and that absence is the boundary
/// keeping this module usable by systems nobody has designed yet.
/// </para>
/// </summary>
public sealed record AssigneeRule
{
    private AssigneeRule(AssigneeStrategy strategy)
    {
        Strategy = strategy;
    }

    public AssigneeStrategy Strategy { get; private init; }

    /// <summary>The user, for <see cref="AssigneeStrategy.User"/>.</summary>
    public Guid? UserId { get; private init; }

    /// <summary>The role, for <see cref="AssigneeStrategy.Role"/>.</summary>
    public Guid? RoleId { get; private init; }

    /// <summary>The position, for <see cref="AssigneeStrategy.Position"/>.</summary>
    public Guid? PositionId { get; private init; }

    /// <summary>The unit, for <see cref="AssigneeStrategy.UnitHead"/>.</summary>
    public Guid? UnitId { get; private init; }

    /// <summary>One named person.</summary>
    public static Result<AssigneeRule> ForUser(Guid userId)
        => userId == Guid.Empty
            ? Result.Failure<AssigneeRule>(WorkflowErrors.AssigneeTargetRequired)
            : Result.Success(new AssigneeRule(AssigneeStrategy.User) { UserId = userId });

    /// <summary>Anyone holding this role. The first to act takes it.</summary>
    public static Result<AssigneeRule> ForRole(Guid roleId)
        => roleId == Guid.Empty
            ? Result.Failure<AssigneeRule>(WorkflowErrors.AssigneeTargetRequired)
            : Result.Success(new AssigneeRule(AssigneeStrategy.Role) { RoleId = roleId });

    /// <summary>Anyone holding this job position.</summary>
    public static Result<AssigneeRule> ForPosition(Guid positionId)
        => positionId == Guid.Empty
            ? Result.Failure<AssigneeRule>(WorkflowErrors.AssigneeTargetRequired)
            : Result.Success(new AssigneeRule(AssigneeStrategy.Position) { PositionId = positionId });

    /// <summary>
    /// Whoever the requester reports to, resolved when the step is entered.
    /// <para>
    /// Resolved then rather than at start, because a request that sits for three
    /// weeks should reach the manager the person has now, not the one they had
    /// when they filed it.
    /// </para>
    /// </summary>
    public static AssigneeRule ForRequesterManager()
        => new(AssigneeStrategy.RequesterManager);

    /// <summary>The head of a named unit.</summary>
    public static Result<AssigneeRule> ForUnitHead(Guid unitId)
        => unitId == Guid.Empty
            ? Result.Failure<AssigneeRule>(WorkflowErrors.AssigneeTargetRequired)
            : Result.Success(new AssigneeRule(AssigneeStrategy.UnitHead) { UnitId = unitId });

    /// <summary>
    /// Whoever the calling application names when it starts the instance.
    /// <para>
    /// This is where business-conditional routing lives — outside the engine.
    /// An application that must send purchases over a threshold to a different
    /// approver works that out itself and supplies the answer.
    /// </para>
    /// </summary>
    public static AssigneeRule ForSuppliedList()
        => new(AssigneeStrategy.SuppliedByCaller);
}

/// <summary>The ways a step can find its assignees.</summary>
public enum AssigneeStrategy
{
    User = 1,
    Role = 2,
    Position = 3,
    RequesterManager = 4,
    UnitHead = 5,

    /// <summary>Named by the calling application at start time.</summary>
    SuppliedByCaller = 6
}
