using CCP.Kernel.Application.Auditing;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Workflow.Application.Abstractions;
using CCP.Modules.Workflow.Domain;
using CCP.Modules.Workflow.Domain.Definitions;
using CCP.Modules.Workflow.Domain.Events;
using CCP.Modules.Workflow.Domain.Instances;

namespace CCP.Modules.Workflow.Application.Engine;

/// <summary>
/// Moving an instance from one step to the next, and making the tasks that go
/// with it.
/// <para>
/// <b>Shared by starting and by acting, because both do the same thing.</b>
/// Entering a step means resolving its assignees, creating a task for each,
/// setting the deadline if the step has one, and telling anyone listening. Two
/// copies of that would drift, and the drift would be tasks that exist for one
/// path into a step and not the other.
/// </para>
/// <para>
/// It holds no business rule and cannot: the only thing it consults is the
/// definition the instance is pinned to.
/// </para>
/// </summary>
public sealed class WorkflowEngine(
    IWorkflowRepository repository,
    IAssigneeResolver assigneeResolver,
    IWorkflowOutbox outbox,
    IClock clock)
{
    /// <summary>
    /// Creates the tasks a step needs and announces them.
    /// <para>
    /// Refuses if nobody resolves. A step with no assignee is a request that
    /// waits forever, and finding that out at the moment it would begin is far
    /// better than the person discovering it three weeks later.
    /// </para>
    /// </summary>
    public async Task<Result> EnterStepAsync(
        WorkflowInstance instance,
        WorkflowStep step,
        IReadOnlyList<Guid> suppliedAssignees,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(step);

        DateTimeOffset now = clock.UtcNow;

        IReadOnlyList<Guid> assignees = await assigneeResolver.ResolveAsync(
            step.Assignee, instance.RequestedBy, suppliedAssignees, cancellationToken);

        if (assignees.Count == 0)
        {
            // A failure rather than a log line. The application layer holds no
            // logging in this codebase, and it does not need to here: the error
            // names the strategy that found nobody, travels to the caller, and
            // is recorded by whoever handles it.
            return Result.Failure(WorkflowErrors.NoAssigneesResolved);
        }

        foreach (Guid assignee in assignees.Distinct())
        {
            Result<WorkflowTask> task = WorkflowTask.Assign(
                instance.Id, step.Key, assignee, step.ServiceLevel, now);

            if (task.IsFailure)
            {
                return Result.Failure(task.Errors);
            }

            repository.AddTask(task.Value);

            await outbox.EnqueueAsync(
                new WorkflowTaskAssignedEvent(
                    task.Value.Id, instance.Id, instance.ApplicationCode,
                    instance.ResourceType, instance.ResourceId, step.Key,
                    assignee, task.Value.DueAt, now),
                cancellationToken);
        }

        return Result.Success();
    }

    /// <summary>
    /// Withdraws the tasks of a step that has been settled.
    /// <para>
    /// When several people could act and one of them did, the others' tasks
    /// stop existing — withdrawn rather than completed, because nobody acted on
    /// them and a report of "who approved what" must not count them. Leaving
    /// them pending would fill inboxes with work that cannot be done.
    /// </para>
    /// </summary>
    public async Task WithdrawRemainingTasksAsync(
        Guid instanceId,
        Guid exceptTaskId,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = clock.UtcNow;

        IReadOnlyList<WorkflowTask> pending =
            await repository.GetPendingTasksAsync(instanceId, cancellationToken);

        foreach (WorkflowTask task in pending)
        {
            if (task.Id != exceptTaskId)
            {
                task.Withdraw(now);
            }
        }
    }

    /// <summary>
    /// Announces that an instance finished, so the application can act on it.
    /// <para>
    /// This is the boundary in one method: the Platform says "approved", and
    /// what that means to a purchase order is entirely the purchasing system's
    /// business.
    /// </para>
    /// </summary>
    public async Task AnnounceCompletionAsync(
        WorkflowInstance instance,
        Guid? decidedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        await outbox.EnqueueAsync(
            new WorkflowInstanceCompletedEvent(
                instance.Id,
                instance.ApplicationCode,
                instance.DefinitionCode,
                instance.DefinitionVersion,
                instance.ResourceType,
                instance.ResourceId,
                instance.Status.ToString(),
                decidedBy,
                clock.UtcNow),
            cancellationToken);
    }

    /// <summary>
    /// The definition an instance is running on — its own version, never the
    /// current one.
    /// </summary>
    public async Task<WorkflowDefinition?> GetPinnedDefinitionAsync(
        WorkflowInstance instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return await repository.FindVersionAsync(
            instance.ApplicationCode,
            instance.DefinitionCode,
            instance.DefinitionVersion,
            cancellationToken);
    }
}

/// <summary>
/// A record that the workflow module is not allowed to hold business rules.
/// <para>
/// Kept as a type rather than a paragraph in a document because the architecture
/// test asserts it: no file in this module may contain a threshold, an amount or
/// a currency. The moment one does, the engine stops being reusable by the next
/// system and starts being the purchasing system's approval logic wearing a
/// general name.
/// </para>
/// </summary>
public static class WorkflowBoundary
{
    /// <summary>Where conditional routing lives instead (§16.3).</summary>
    public const string ConditionalRoutingBelongsToTheCaller =
        "Routing that depends on business data is decided by the calling application, "
        + "which supplies the assignees at start time or answers a callback. The engine "
        + "resolves people by their place in the organization and nothing else.";

    /// <summary>Auditing is not optional here (§16.5).</summary>
    public const string EveryActionIsAudited =
        "Every action on an instance is recorded with actor, time and comment, and the "
        + "record is append-only. The history of a decision is the decision's "
        + "justification, and an editable history justifies nothing.";
}

/// <summary>The module name every audit entry from Workflow carries.</summary>
public static class WorkflowAudit
{
    public const string ModuleName = "workflow";

    /// <summary>An entry for an action on an instance.</summary>
    public static AuditEntry Action(
        string action,
        Guid instanceId,
        string detail,
        AuditOutcome outcome = AuditOutcome.Success)
        => new(ModuleName, action, outcome, "workflow-instance", instanceId.ToString(),
            NewValue: detail);
}
