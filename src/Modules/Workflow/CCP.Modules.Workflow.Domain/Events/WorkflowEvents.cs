using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;

namespace CCP.Modules.Workflow.Domain.Events;

/// <summary>
/// Base for Workflow integration events.
/// <para>
/// <b>These are how the calling application finds out.</b> The Platform decides
/// <i>that</i> something was approved; the application decides <i>what that
/// means</i> — releasing a purchase order, paying an invoice, granting leave
/// (§16.4). So every event carries the resource type and id the application gave
/// at the start, because that is the only handle it has on its own record.
/// </para>
/// <para>
/// The definition code and version travel too. An application processing a
/// completion months later needs to know which version of the process produced
/// it, and the answer must be in the message rather than in a lookup against a
/// definition that may since have been retired.
/// </para>
/// </summary>
public abstract record WorkflowEvent : IIntegrationEvent
{
    protected WorkflowEvent(DateTimeOffset occurredAt)
    {
        EventId = Uuid7.NewGuid(occurredAt);
        OccurredAt = occurredAt;
    }

    public Guid EventId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public abstract string EventType { get; }
}

/// <summary>A process was started against a business record.</summary>
public sealed record WorkflowInstanceStartedEvent(
    Guid InstanceId,
    string ApplicationCode,
    string DefinitionCode,
    int DefinitionVersion,
    string ResourceType,
    string ResourceId,
    Guid RequestedBy,
    string InitialStepKey,
    DateTimeOffset At) : WorkflowEvent(At)
{
    public override string EventType => "workflow.instance.started";
}

/// <summary>
/// A process finished.
/// <para>
/// The one event most applications actually wait for. <c>Outcome</c> is the
/// whole message: approved, rejected or cancelled are three different facts and
/// a business system does three different things with them.
/// </para>
/// </summary>
public sealed record WorkflowInstanceCompletedEvent(
    Guid InstanceId,
    string ApplicationCode,
    string DefinitionCode,
    int DefinitionVersion,
    string ResourceType,
    string ResourceId,
    string Outcome,
    Guid? DecidedBy,
    DateTimeOffset At) : WorkflowEvent(At)
{
    public override string EventType => "workflow.instance.completed";
}

/// <summary>A process moved from one step to the next.</summary>
public sealed record WorkflowStepAdvancedEvent(
    Guid InstanceId,
    string ApplicationCode,
    string ResourceType,
    string ResourceId,
    string FromStepKey,
    string ToStepKey,
    string Action,
    Guid ActorUserId,
    DateTimeOffset At) : WorkflowEvent(At)
{
    public override string EventType => "workflow.step.advanced";
}

/// <summary>
/// Somebody has something to do.
/// <para>
/// Raised so Notifications (Phase 9) can tell them, without Workflow knowing
/// that notifications exist.
/// </para>
/// </summary>
public sealed record WorkflowTaskAssignedEvent(
    Guid TaskId,
    Guid InstanceId,
    string ApplicationCode,
    string ResourceType,
    string ResourceId,
    string StepKey,
    Guid AssignedToUserId,
    DateTimeOffset? DueAt,
    DateTimeOffset At) : WorkflowEvent(At)
{
    public override string EventType => "workflow.task.assigned";
}

/// <summary>A task passed its service level without being acted on.</summary>
public sealed record WorkflowTaskEscalatedEvent(
    Guid TaskId,
    Guid InstanceId,
    string ApplicationCode,
    string StepKey,
    Guid AssignedToUserId,
    DateTimeOffset DueAt,
    DateTimeOffset At) : WorkflowEvent(At)
{
    public override string EventType => "workflow.task.escalated";
}

/// <summary>A task was handed to somebody else.</summary>
public sealed record WorkflowTaskDelegatedEvent(
    Guid TaskId,
    Guid InstanceId,
    Guid FromUserId,
    Guid ToUserId,
    DateTimeOffset At) : WorkflowEvent(At)
{
    public override string EventType => "workflow.task.delegated";
}
