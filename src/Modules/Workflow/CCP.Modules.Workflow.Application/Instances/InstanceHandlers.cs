using CCP.Kernel.Application.Auditing;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Workflow.Application.Abstractions;
using CCP.Modules.Workflow.Application.Engine;
using CCP.Modules.Workflow.Contracts.Dtos;
using CCP.Modules.Workflow.Domain;
using CCP.Modules.Workflow.Domain.Definitions;
using CCP.Modules.Workflow.Contracts.Events;
using CCP.Modules.Workflow.Domain.Instances;

namespace CCP.Modules.Workflow.Application.Instances;

/// <summary>Starting a process against a business record.</summary>
public sealed record StartInstanceCommand(
    string ApplicationCode,
    string DefinitionCode,
    string ResourceType,
    string ResourceId,
    Guid RequestedBy,
    IReadOnlyList<Guid> SuppliedAssignees);

/// <summary>Acting on a task.</summary>
public sealed record ActOnTaskCommand(
    Guid TaskId,
    WorkflowActionType Action,
    Guid ActorUserId,
    string? Comment,
    Guid? DelegateToUserId);

/// <summary>Withdrawing a request.</summary>
public sealed record CancelInstanceCommand(Guid InstanceId, Guid ActorUserId, string? Reason);

/// <summary>
/// Starting an instance.
/// <para>
/// <b>The definition is chosen here, once, and pinned.</b> The newest published
/// version is taken and its number written onto the instance, which then runs on
/// that version for the rest of its life. A definition published tomorrow does
/// not touch a request filed today (§16.5).
/// </para>
/// <para>
/// The first step is entered inside the same transaction, so a started instance
/// always has its tasks. An instance that exists with nobody assigned is a
/// request sitting silently in a database, which is worse than one that was
/// refused.
/// </para>
/// </summary>
public sealed class StartInstanceHandler(
    IWorkflowRepository repository,
    WorkflowEngine engine,
    IWorkflowOutbox outbox,
    IWorkflowUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IClock clock)
{
    public async Task<Result<WorkflowInstanceDto>> HandleAsync(
        StartInstanceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        WorkflowDefinition? definition = await repository.FindLatestPublishedAsync(
            command.ApplicationCode, command.DefinitionCode, cancellationToken);

        if (definition is null)
        {
            return Result.Failure<WorkflowInstanceDto>(WorkflowErrors.DefinitionNotFound);
        }

        Result<WorkflowInstance> instance = WorkflowInstance.Start(
            definition, command.ResourceType, command.ResourceId,
            command.RequestedBy, clock.UtcNow);

        if (instance.IsFailure)
        {
            return Result.Failure<WorkflowInstanceDto>(instance.Errors);
        }

        WorkflowStep? initial = definition.FindStep(definition.InitialStepKey!);

        if (initial is null)
        {
            // Publication validates this, so reaching it means the definition
            // was changed underneath a published version — which the aggregate
            // refuses. Handled rather than assumed away, because "cannot happen"
            // is how a null reference reaches production.
            return Result.Failure<WorkflowInstanceDto>(
                WorkflowErrors.StepNotFound(definition.InitialStepKey!));
        }

        // The first step may be the one that asks the application who should
        // act. Saying so here is worth a branch: without it the caller gets
        // "nobody could be found for this step -- check that the role, position
        // or manager it names still exists", which is advice about a role this
        // step does not have, sending somebody to look at the org chart for a
        // field they forgot to send.
        if (initial.Assignee.Strategy == AssigneeStrategy.SuppliedByCaller
            && command.SuppliedAssignees.Count == 0)
        {
            return Result.Failure<WorkflowInstanceDto>(WorkflowErrors.SuppliedAssigneesRequired);
        }

        repository.AddInstance(instance.Value);

        Result entered = await engine.EnterStepAsync(
            instance.Value, initial, command.SuppliedAssignees, cancellationToken);

        if (entered.IsFailure)
        {
            // Nothing is saved, so nothing exists. The caller is told why —
            // usually that the step's role or position resolves to nobody — and
            // can fix the structure rather than hunt for a stalled request.
            return Result.Failure<WorkflowInstanceDto>(entered.Errors);
        }

        await outbox.EnqueueAsync(
            new WorkflowInstanceStartedEvent(
                instance.Value.Id, definition.ApplicationCode, definition.Code,
                definition.Version, instance.Value.ResourceType, instance.Value.ResourceId,
                command.RequestedBy, initial.Key, clock.UtcNow),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditTrail.RecordAsync(
            WorkflowAudit.Action(
                "instance.started",
                instance.Value.Id,
                $$"""{"definition":"{{definition.Code}}","version":{{definition.Version}},"resource":"{{instance.Value.ResourceType}}/{{instance.Value.ResourceId}}"}"""),
            cancellationToken);

        return Result.Success(InstanceMapper.ToDto(instance.Value));
    }
}

/// <summary>
/// Acting on a task: approve, reject, return, delegate or comment.
/// <para>
/// <b>The whole of the state machine passes through here</b>, and every check
/// it makes is one somebody could otherwise get wrong: is this task yours, is
/// the instance still running, is the instance still at this step, does the
/// definition permit this action here, and — if it moves — who does it move to.
/// </para>
/// <para>
/// <b>The definition consulted is the instance's own version.</b> Reading the
/// current one would mean an approval evaluated against rules that did not exist
/// when it was requested.
/// </para>
/// </summary>
public sealed class ActOnTaskHandler(
    IWorkflowRepository repository,
    WorkflowEngine engine,
    IWorkflowOutbox outbox,
    IWorkflowUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IClock clock)
{
    public async Task<Result<WorkflowInstanceDto>> HandleAsync(
        ActOnTaskCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        WorkflowTask? task = await repository.FindTaskAsync(command.TaskId, cancellationToken);

        if (task is null)
        {
            return Result.Failure<WorkflowInstanceDto>(WorkflowErrors.TaskNotFound);
        }

        if (!task.MayBeActedOnBy(command.ActorUserId))
        {
            // Recorded as denied, not merely refused. Somebody acting on a task
            // that is not theirs is either confused or probing, and both are
            // worth being able to see later.
            await auditTrail.RecordAsync(
                new AuditEntry(
                    WorkflowAudit.ModuleName,
                    "task.action",
                    AuditOutcome.Denied,
                    "workflow-task",
                    task.Id.ToString(),
                    Metadata: $$"""{"action":"{{command.Action}}","assignedTo":"{{task.AssignedToUserId}}"}"""),
                cancellationToken);

            return Result.Failure<WorkflowInstanceDto>(
                task.Status == Domain.Instances.WorkflowTaskStatus.Pending
                    ? WorkflowErrors.NotTheAssignee
                    : WorkflowErrors.TaskNotPending);
        }

        WorkflowInstance? instance = await repository.FindInstanceAsync(
            task.InstanceId, cancellationToken);

        if (instance is null)
        {
            return Result.Failure<WorkflowInstanceDto>(WorkflowErrors.InstanceNotFound);
        }

        WorkflowDefinition? definition =
            await engine.GetPinnedDefinitionAsync(instance, cancellationToken);

        WorkflowStep? step = definition?.FindStep(task.StepKey);

        if (step is null)
        {
            return Result.Failure<WorkflowInstanceDto>(
                WorkflowErrors.StepNotFound(task.StepKey));
        }

        DateTimeOffset now = clock.UtcNow;
        string fromStep = task.StepKey;

        // Delegation moves the task, not the instance.
        if (command.Action == WorkflowActionType.Delegate)
        {
            if (command.DelegateToUserId is not { } newAssignee)
            {
                return Result.Failure<WorkflowInstanceDto>(WorkflowErrors.InvalidDelegate);
            }

            Result delegated = task.DelegateTo(command.ActorUserId, newAssignee, now);

            if (delegated.IsFailure)
            {
                return Result.Failure<WorkflowInstanceDto>(delegated.Errors);
            }

            instance.Act(step, WorkflowActionType.Delegate, command.ActorUserId, command.Comment, now);

            await outbox.EnqueueAsync(
                new WorkflowTaskDelegatedEvent(
                    task.Id, instance.Id, command.ActorUserId, newAssignee, now),
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            await auditTrail.RecordAsync(
                WorkflowAudit.Action(
                    "task.delegated", instance.Id,
                    $$"""{"task":"{{task.Id}}","to":"{{newAssignee}}"}"""),
                cancellationToken);

            return Result.Success(InstanceMapper.ToDto(instance));
        }

        // A comment settles nothing and leaves the task open, because saying
        // something about a decision is not taking it.
        if (command.Action == WorkflowActionType.Comment)
        {
            Result<WorkflowInstanceAction> noted = instance.Act(
                step, WorkflowActionType.Comment, command.ActorUserId, command.Comment, now);

            if (noted.IsFailure)
            {
                return Result.Failure<WorkflowInstanceDto>(noted.Errors);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(InstanceMapper.ToDto(instance));
        }

        Result<WorkflowInstanceAction> acted = instance.Act(
            step, command.Action, command.ActorUserId, command.Comment, now);

        if (acted.IsFailure)
        {
            return Result.Failure<WorkflowInstanceDto>(acted.Errors);
        }

        Result completed = task.Complete(command.Action, command.ActorUserId, now);

        if (completed.IsFailure)
        {
            return Result.Failure<WorkflowInstanceDto>(completed.Errors);
        }

        // Everyone else who could have taken this step no longer needs to.
        await engine.WithdrawRemainingTasksAsync(instance.Id, task.Id, cancellationToken);

        if (instance.CurrentStepKey is { } next)
        {
            WorkflowStep? nextStep = definition!.FindStep(next);

            if (nextStep is null)
            {
                return Result.Failure<WorkflowInstanceDto>(WorkflowErrors.StepNotFound(next));
            }

            // Supplied assignees are a start-time concept and are deliberately
            // not carried forward: a step reached later resolves from the
            // organization, or the application starts a new instance.
            Result entered = await engine.EnterStepAsync(instance, nextStep, [], cancellationToken);

            if (entered.IsFailure)
            {
                return Result.Failure<WorkflowInstanceDto>(entered.Errors);
            }

            await outbox.EnqueueAsync(
                new WorkflowStepAdvancedEvent(
                    instance.Id, instance.ApplicationCode, instance.ResourceType,
                    instance.ResourceId, fromStep, next, command.Action.ToString(),
                    command.ActorUserId, now),
                cancellationToken);
        }
        else
        {
            await engine.AnnounceCompletionAsync(instance, command.ActorUserId, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditTrail.RecordAsync(
            WorkflowAudit.Action(
                "task.action", instance.Id,
                $$"""{"action":"{{command.Action}}","step":"{{fromStep}}","status":"{{instance.Status}}"}"""),
            cancellationToken);

        return Result.Success(InstanceMapper.ToDto(instance));
    }
}

/// <summary>
/// Withdrawing a request.
/// <para>
/// Allowed to the requester, and to anyone the endpoint's permission admits.
/// Not modelled as a transition the definition must remember to allow: a person
/// withdrawing their own request should not depend on whoever wrote the process
/// having thought of it at every step.
/// </para>
/// </summary>
public sealed class CancelInstanceHandler(
    IWorkflowRepository repository,
    WorkflowEngine engine,
    IWorkflowUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        CancelInstanceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        WorkflowInstance? instance = await repository.FindInstanceAsync(
            command.InstanceId, cancellationToken);

        if (instance is null)
        {
            return Result.Failure(WorkflowErrors.InstanceNotFound);
        }

        Result cancelled = instance.Cancel(command.ActorUserId, command.Reason, clock.UtcNow);

        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        // Every outstanding task goes with it. Leaving them would fill inboxes
        // with approvals for a request that no longer exists.
        await engine.WithdrawRemainingTasksAsync(instance.Id, Guid.Empty, cancellationToken);

        await engine.AnnounceCompletionAsync(instance, command.ActorUserId, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditTrail.RecordAsync(
            WorkflowAudit.Action("instance.cancelled", instance.Id, "{}"),
            cancellationToken);

        return Result.Success();
    }
}
