using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;

namespace CCP.Modules.Workflow.Domain.Definitions;

/// <summary>
/// One state in a process, and who has to act in it.
/// <para>
/// A step names the people it waits for and the moves it permits. It does not
/// name a condition, because a condition about business data is a business rule
/// and this module holds none (§16.3).
/// </para>
/// </summary>
public sealed class WorkflowStep : Entity
{
    private readonly List<WorkflowTransition> _transitions = [];

    private WorkflowStep() { }

    private WorkflowStep(
        Guid id,
        Guid definitionId,
        string key,
        string nameAr,
        string nameEn,
        int order,
        AssigneeRule assignee,
        DateTimeOffset now)
        : base(id)
    {
        DefinitionId = definitionId;
        Key = key;
        NameAr = nameAr;
        NameEn = nameEn;
        Order = order;
        Assignee = assignee;
        CreatedAt = now;
    }

    public Guid DefinitionId { get; private set; }

    /// <summary>Stable within the definition. Instances refer to it by name.</summary>
    public string Key { get; private set; } = string.Empty;

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    /// <summary>For display only. Order does not imply a transition.</summary>
    public int Order { get; private set; }

    public AssigneeRule Assignee { get; private set; } = null!;

    /// <summary>
    /// How long this step may sit before it is escalated, if at all.
    /// <para>
    /// Absent by default. A deadline nobody agreed to is a stream of
    /// notifications people learn to ignore.
    /// </para>
    /// </summary>
    public TimeSpan? ServiceLevel { get; private set; }

    public IReadOnlyList<WorkflowTransition> Transitions => _transitions.AsReadOnly();

    public DateTimeOffset CreatedAt { get; private set; }

    public static Result<WorkflowStep> Create(
        Guid definitionId,
        string key,
        string nameAr,
        string nameEn,
        int order,
        AssigneeRule assignee,
        TimeSpan? serviceLevel,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(assignee);

        if (string.IsNullOrWhiteSpace(key))
        {
            return Result.Failure<WorkflowStep>(WorkflowErrors.StepKeyRequired);
        }

        if (string.IsNullOrWhiteSpace(nameAr) || string.IsNullOrWhiteSpace(nameEn))
        {
            return Result.Failure<WorkflowStep>(WorkflowErrors.StepNameRequired);
        }

        if (serviceLevel is { } level && level <= TimeSpan.Zero)
        {
            return Result.Failure<WorkflowStep>(WorkflowErrors.ServiceLevelMustBePositive);
        }

        return Result.Success(new WorkflowStep(
            Uuid7.NewGuid(now), definitionId, key.Trim(), nameAr.Trim(), nameEn.Trim(),
            order, assignee, now)
        {
            ServiceLevel = serviceLevel
        });
    }

    /// <summary>Permits an action, and says where it leads.</summary>
    public Result AddTransition(WorkflowTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        if (_transitions.Any(t => t.Action == transition.Action))
        {
            // One outcome per action per step. Two "approve" transitions from
            // one step is a process whose next state depends on something the
            // definition does not say, which is exactly the ambiguity this
            // module refuses to hold.
            return Result.Failure(WorkflowErrors.DuplicateTransition(Key, transition.Action));
        }

        _transitions.Add(transition);

        return Result.Success();
    }

    /// <summary>Whether this action is permitted here, and where it goes.</summary>
    public WorkflowTransition? FindTransition(WorkflowActionType action)
        => _transitions.FirstOrDefault(t => t.Action == action);
}

/// <summary>
/// An allowed move out of a step.
/// <para>
/// A null target ends the instance. That is how a terminal step is expressed —
/// rather than a separate step type, which would need its own rules everywhere
/// a step is handled.
/// </para>
/// </summary>
public sealed class WorkflowTransition : Entity
{
    private WorkflowTransition() { }

    private WorkflowTransition(Guid id, Guid stepId, WorkflowActionType action, string? targetStepKey)
        : base(id)
    {
        StepId = stepId;
        Action = action;
        TargetStepKey = targetStepKey;
    }

    public Guid StepId { get; private set; }

    public WorkflowActionType Action { get; private set; }

    /// <summary>Where the instance goes. Null completes it.</summary>
    public string? TargetStepKey { get; private set; }

    public static WorkflowTransition Create(
        Guid stepId,
        WorkflowActionType action,
        string? targetStepKey,
        DateTimeOffset now)
        => new(Uuid7.NewGuid(now), stepId, action,
            string.IsNullOrWhiteSpace(targetStepKey) ? null : targetStepKey.Trim());
}

/// <summary>
/// What someone can do to a task.
/// <para>
/// A closed set, deliberately. An open set of application-defined verbs would
/// mean the engine could not validate a transition, and validating transitions
/// is most of what it is for.
/// </para>
/// </summary>
public enum WorkflowActionType
{
    /// <summary>Agreed. The instance moves on.</summary>
    Approve = 1,

    /// <summary>Refused. Normally terminal.</summary>
    Reject = 2,

    /// <summary>Sent back for correction, usually to an earlier step.</summary>
    Return = 3,

    /// <summary>Handed to someone else. The step does not move.</summary>
    Delegate = 4,

    /// <summary>Said something. Nothing moves.</summary>
    Comment = 5,

    /// <summary>Withdrawn by the requester or an administrator. Terminal.</summary>
    Cancel = 6
}
