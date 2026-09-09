using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Workflow.Domain.Definitions;

namespace CCP.Modules.Workflow.Domain.Instances;

/// <summary>
/// Something waiting for one person to do.
/// <para>
/// A step produces one task per resolved assignee. When several people could
/// act — everyone holding a role, say — they each get a task and the first to
/// act settles it; the rest are withdrawn rather than left in inboxes as work
/// that no longer exists.
/// </para>
/// <para>
/// <b>Only the assignee or their delegate may act.</b> That is checked here and
/// again by the handler, because it is the one rule whose failure means somebody
/// approved something that was never theirs to approve.
/// </para>
/// </summary>
public sealed class WorkflowTask : AggregateRoot, IAuditableEntity
{
    private WorkflowTask() { }

    private WorkflowTask(
        Guid id,
        Guid instanceId,
        string stepKey,
        Guid assignedToUserId,
        DateTimeOffset now,
        DateTimeOffset? dueAt)
        : base(id)
    {
        InstanceId = instanceId;
        StepKey = stepKey;
        AssignedToUserId = assignedToUserId;
        Status = TaskStatus.Pending;
        AssignedAt = now;
        DueAt = dueAt;
        CreatedAt = now;
    }

    public Guid InstanceId { get; private set; }

    public string StepKey { get; private set; } = string.Empty;

    /// <summary>Who must act. Changes when the task is delegated.</summary>
    public Guid AssignedToUserId { get; private set; }

    /// <summary>
    /// Who it was assigned to before a delegation, if any.
    /// <para>
    /// Kept so the trail reads "Amira delegated to Faisal, Faisal approved"
    /// rather than "Faisal approved" with no explanation of why he could.
    /// </para>
    /// </summary>
    public Guid? DelegatedFromUserId { get; private set; }

    public TaskStatus Status { get; private set; }

    public DateTimeOffset AssignedAt { get; private set; }

    /// <summary>When the step's service level expires, if it has one.</summary>
    public DateTimeOffset? DueAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public WorkflowActionType? CompletedWith { get; private set; }

    /// <summary>
    /// Whether this task has been escalated for missing its service level.
    /// <para>
    /// Recorded so escalation happens once. A timer that fires every sweep
    /// produces a notification every few minutes until somebody acts, which
    /// teaches people to filter the notifications.
    /// </para>
    /// </summary>
    public DateTimeOffset? EscalatedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static Result<WorkflowTask> Assign(
        Guid instanceId,
        string stepKey,
        Guid assignedToUserId,
        TimeSpan? serviceLevel,
        DateTimeOffset now)
    {
        if (assignedToUserId == Guid.Empty)
        {
            return Result.Failure<WorkflowTask>(WorkflowErrors.AssigneeRequired);
        }

        if (string.IsNullOrWhiteSpace(stepKey))
        {
            return Result.Failure<WorkflowTask>(WorkflowErrors.StepKeyRequired);
        }

        return Result.Success(new WorkflowTask(
            Uuid7.NewGuid(now), instanceId, stepKey.Trim(), assignedToUserId, now,
            serviceLevel is { } level ? now.Add(level) : null));
    }

    /// <summary>Whether this person may act on this task.</summary>
    public bool MayBeActedOnBy(Guid userId)
        => Status == TaskStatus.Pending && AssignedToUserId == userId;

    /// <summary>Settles the task with the action that closed it.</summary>
    public Result Complete(WorkflowActionType action, Guid actorUserId, DateTimeOffset now)
    {
        if (Status != TaskStatus.Pending)
        {
            return Result.Failure(WorkflowErrors.TaskNotPending);
        }

        if (AssignedToUserId != actorUserId)
        {
            // Checked here as well as in the handler. This is the rule whose
            // failure means somebody approved something that was never theirs,
            // and one check is one place to forget it.
            return Result.Failure(WorkflowErrors.NotTheAssignee);
        }

        Status = TaskStatus.Completed;
        CompletedWith = action;
        CompletedAt = now;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Hands the task to somebody else.
    /// <para>
    /// The step does not move and the instance does not advance: delegation
    /// changes who is being waited for, not what is being waited for.
    /// </para>
    /// </summary>
    public Result DelegateTo(Guid actorUserId, Guid newAssigneeUserId, DateTimeOffset now)
    {
        if (Status != TaskStatus.Pending)
        {
            return Result.Failure(WorkflowErrors.TaskNotPending);
        }

        if (AssignedToUserId != actorUserId)
        {
            return Result.Failure(WorkflowErrors.NotTheAssignee);
        }

        if (newAssigneeUserId == Guid.Empty || newAssigneeUserId == actorUserId)
        {
            return Result.Failure(WorkflowErrors.InvalidDelegate);
        }

        DelegatedFromUserId = actorUserId;
        AssignedToUserId = newAssigneeUserId;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Withdraws a task nobody needs to do any more.
    /// <para>
    /// Used when a colleague settled the step first, or the instance was
    /// cancelled. Not the same as completing it: nobody acted, and a report of
    /// "who approved what" must not count this.
    /// </para>
    /// </summary>
    public Result Withdraw(DateTimeOffset now)
    {
        if (Status != TaskStatus.Pending)
        {
            return Result.Failure(WorkflowErrors.TaskNotPending);
        }

        Status = TaskStatus.Withdrawn;
        CompletedAt = now;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>Marks that the service level was missed and escalation has fired.</summary>
    public Result Escalate(DateTimeOffset now)
    {
        if (Status != TaskStatus.Pending)
        {
            return Result.Failure(WorkflowErrors.TaskNotPending);
        }

        if (EscalatedAt is not null)
        {
            // Once. A timer that fires on every sweep sends a notification every
            // few minutes until somebody acts, and people learn to filter it.
            return Result.Failure(WorkflowErrors.TaskAlreadyEscalated);
        }

        EscalatedAt = now;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>Whether this task has passed its due time without being acted on.</summary>
    public bool IsOverdue(DateTimeOffset now)
        => Status == TaskStatus.Pending && DueAt is { } due && now > due;
}

/// <summary>What has become of a task.</summary>
public enum TaskStatus
{
    /// <summary>Waiting for its assignee.</summary>
    Pending = 1,

    /// <summary>Acted on.</summary>
    Completed = 2,

    /// <summary>No longer needed — a colleague acted, or the instance ended.</summary>
    Withdrawn = 3
}
