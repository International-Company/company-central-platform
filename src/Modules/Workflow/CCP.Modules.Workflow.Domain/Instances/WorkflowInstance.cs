using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Workflow.Domain.Definitions;

namespace CCP.Modules.Workflow.Domain.Instances;

/// <summary>
/// One run of a process, on the version it started with.
/// <para>
/// <b>The version is recorded and never changes</b> (§16.5). A definition edited
/// while this is in flight does not touch it: an approval whose rules changed
/// underneath it is an approval nobody can account for afterwards, and "why did
/// this go to Amira?" must have an answer a year later.
/// </para>
/// <para>
/// The business resource is a type and an id, and both are opaque. The Platform
/// records that <c>purchase-order/6f3e…</c> was approved; it does not know what a
/// purchase order is, cannot read one, and has no opinion about what approval
/// means for it. The application decides that when it receives the completion
/// event (§16.4).
/// </para>
/// </summary>
public sealed class WorkflowInstance : AggregateRoot, IAuditableEntity
{
    private readonly List<WorkflowInstanceAction> _actions = [];

    private WorkflowInstance() { }

    private WorkflowInstance(
        Guid id,
        Guid definitionId,
        string definitionCode,
        int definitionVersion,
        string applicationCode,
        string resourceType,
        string resourceId,
        Guid requestedBy,
        string currentStepKey,
        DateTimeOffset now)
        : base(id)
    {
        DefinitionId = definitionId;
        DefinitionCode = definitionCode;
        DefinitionVersion = definitionVersion;
        ApplicationCode = applicationCode;
        ResourceType = resourceType;
        ResourceId = resourceId;
        RequestedBy = requestedBy;
        CurrentStepKey = currentStepKey;
        Status = InstanceStatus.Running;
        StartedAt = now;
        CreatedAt = now;
    }

    public Guid DefinitionId { get; private set; }

    /// <summary>Copied, not joined. The definition may be retired or renamed.</summary>
    public string DefinitionCode { get; private set; } = string.Empty;

    /// <summary>The version this instance runs on, for its whole life.</summary>
    public int DefinitionVersion { get; private set; }

    public string ApplicationCode { get; private set; } = string.Empty;

    /// <summary>What kind of thing is being approved. Opaque to the Platform.</summary>
    public string ResourceType { get; private set; } = string.Empty;

    /// <summary>Which one. Opaque, and never dereferenced.</summary>
    public string ResourceId { get; private set; } = string.Empty;

    public Guid RequestedBy { get; private set; }

    /// <summary>Where it is now. Null once it has finished.</summary>
    public string? CurrentStepKey { get; private set; }

    public InstanceStatus Status { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Everything that happened, in order.</summary>
    public IReadOnlyList<WorkflowInstanceAction> Actions => _actions.AsReadOnly();

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static Result<WorkflowInstance> Start(
        WorkflowDefinition definition,
        string resourceType,
        string resourceId,
        Guid requestedBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.Status != DefinitionStatus.Published)
        {
            // A draft has not been validated and a retired version is closed to
            // new work. Neither refusal is about the request; both are about the
            // process it would run on.
            return Result.Failure<WorkflowInstance>(WorkflowErrors.DefinitionNotPublished);
        }

        if (string.IsNullOrWhiteSpace(resourceType) || string.IsNullOrWhiteSpace(resourceId))
        {
            return Result.Failure<WorkflowInstance>(WorkflowErrors.ResourceRequired);
        }

        if (requestedBy == Guid.Empty)
        {
            return Result.Failure<WorkflowInstance>(WorkflowErrors.RequesterRequired);
        }

        if (definition.InitialStepKey is not { } initial)
        {
            return Result.Failure<WorkflowInstance>(WorkflowErrors.DefinitionHasNoInitialStep);
        }

        return Result.Success(new WorkflowInstance(
            Uuid7.NewGuid(now),
            definition.Id,
            definition.Code,
            definition.Version,
            definition.ApplicationCode,
            resourceType.Trim(),
            resourceId.Trim(),
            requestedBy,
            initial,
            now));
    }

    /// <summary>
    /// Records what someone did and moves the instance if the action says to.
    /// <para>
    /// <b>The definition decides, not the caller.</b> An action is permitted
    /// only if the current step has a transition for it; anything else is
    /// refused and the refusal is worth recording, because an attempt to act
    /// out of turn is either a confused user or a client with a stale view.
    /// </para>
    /// <para>
    /// The step is passed in rather than looked up: the instance holds a version
    /// number, not the definition, precisely so that nothing here can
    /// accidentally read a newer one.
    /// </para>
    /// </summary>
    public Result<WorkflowInstanceAction> Act(
        WorkflowStep step,
        WorkflowActionType action,
        Guid actorUserId,
        string? comment,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(step);

        if (Status != InstanceStatus.Running)
        {
            return Result.Failure<WorkflowInstanceAction>(WorkflowErrors.InstanceNotRunning);
        }

        if (!string.Equals(step.Key, CurrentStepKey, StringComparison.Ordinal))
        {
            return Result.Failure<WorkflowInstanceAction>(
                WorkflowErrors.StepIsNotCurrent(step.Key));
        }

        // A comment changes nothing and is always allowed: saying something
        // about a decision in progress is not a decision.
        if (action == WorkflowActionType.Comment)
        {
            var note = WorkflowInstanceAction.Record(
                Id, step.Key, action, actorUserId, comment, now);

            _actions.Add(note);

            return Result.Success(note);
        }

        // Delegation hands the task to someone else and leaves the instance
        // exactly where it is. It is recorded on the task, not here.
        if (action == WorkflowActionType.Delegate)
        {
            var delegated = WorkflowInstanceAction.Record(
                Id, step.Key, action, actorUserId, comment, now);

            _actions.Add(delegated);
            UpdatedAt = now;

            return Result.Success(delegated);
        }

        WorkflowTransition? transition = step.FindTransition(action);

        if (transition is null)
        {
            return Result.Failure<WorkflowInstanceAction>(
                WorkflowErrors.ActionNotPermitted(step.Key, action));
        }

        var recorded = WorkflowInstanceAction.Record(
            Id, step.Key, action, actorUserId, comment, now);

        _actions.Add(recorded);

        if (transition.TargetStepKey is { } target)
        {
            CurrentStepKey = target;
        }
        else
        {
            // No target: this action ends the instance. Which ending it is comes
            // from the action, because "approved" and "rejected" are different
            // facts and a caller reading the record needs to know which.
            //
            // Every action is named. The first version let anything unnamed fall
            // through to Approved, which meant a `Return` transition with no
            // target — a definition mistake, but one a person could make — would
            // have recorded a request as approved that somebody had just sent
            // back. An architecture test found it before it could.
            Result<InstanceStatus> ending = action switch
            {
                WorkflowActionType.Approve => Result.Success(InstanceStatus.Approved),
                WorkflowActionType.Reject => Result.Success(InstanceStatus.Rejected),
                WorkflowActionType.Cancel => Result.Success(InstanceStatus.Cancelled),

                // Returning means "go back and fix this". Going back to nowhere
                // is not an ending, and treating it as one would silently decide
                // an approval nobody made.
                WorkflowActionType.Return =>
                    Result.Failure<InstanceStatus>(WorkflowErrors.ReturnNeedsATarget(step.Key)),

                _ => Result.Failure<InstanceStatus>(
                    WorkflowErrors.ActionNotPermitted(step.Key, action))
            };

            if (ending.IsFailure)
            {
                // The action is not recorded: it did not happen. Rolling the
                // record back here rather than leaving a half-applied action is
                // what keeps the history honest.
                _actions.Remove(recorded);

                return Result.Failure<WorkflowInstanceAction>(ending.Errors);
            }

            CurrentStepKey = null;
            CompletedAt = now;
            Status = ending.Value;
        }

        UpdatedAt = now;

        return Result.Success(recorded);
    }

    /// <summary>
    /// Ends the instance without a decision.
    /// <para>
    /// Separate from <see cref="WorkflowActionType.Cancel"/> as a transition,
    /// because a requester withdrawing their own request should not depend on
    /// the definition having thought to allow it at every step.
    /// </para>
    /// </summary>
    public Result Cancel(Guid actorUserId, string? reason, DateTimeOffset now)
    {
        if (Status != InstanceStatus.Running)
        {
            return Result.Failure(WorkflowErrors.InstanceNotRunning);
        }

        _actions.Add(WorkflowInstanceAction.Record(
            Id, CurrentStepKey ?? string.Empty, WorkflowActionType.Cancel,
            actorUserId, reason, now));

        CurrentStepKey = null;
        Status = InstanceStatus.Cancelled;
        CompletedAt = now;
        UpdatedAt = now;

        return Result.Success();
    }
}

/// <summary>Where an instance has got to.</summary>
public enum InstanceStatus
{
    Running = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4
}

/// <summary>
/// One thing that happened to an instance.
/// <para>
/// Append-only. The history of a decision is the decision's justification, and
/// an editable history justifies nothing.
/// </para>
/// </summary>
public sealed class WorkflowInstanceAction : Entity
{
    private WorkflowInstanceAction() { }

    private WorkflowInstanceAction(
        Guid id,
        Guid instanceId,
        string stepKey,
        WorkflowActionType action,
        Guid actorUserId,
        string? comment,
        DateTimeOffset now)
        : base(id)
    {
        InstanceId = instanceId;
        StepKey = stepKey;
        Action = action;
        ActorUserId = actorUserId;
        Comment = comment;
        OccurredAt = now;
    }

    public Guid InstanceId { get; private set; }

    /// <summary>Which step it happened in, by name rather than by id.</summary>
    public string StepKey { get; private set; } = string.Empty;

    public WorkflowActionType Action { get; private set; }

    public Guid ActorUserId { get; private set; }

    public string? Comment { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    internal static WorkflowInstanceAction Record(
        Guid instanceId,
        string stepKey,
        WorkflowActionType action,
        Guid actorUserId,
        string? comment,
        DateTimeOffset now)
        => new(Uuid7.NewGuid(now), instanceId, stepKey, action, actorUserId,
            string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(), now);
}
