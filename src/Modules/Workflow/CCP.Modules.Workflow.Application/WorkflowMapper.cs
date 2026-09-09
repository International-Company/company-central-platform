using CCP.Modules.Workflow.Contracts.Dtos;
using CCP.Modules.Workflow.Domain.Definitions;
using CCP.Modules.Workflow.Domain.Instances;

namespace CCP.Modules.Workflow.Application;

/// <summary>
/// Turns the module's aggregates into what callers see.
/// <para>
/// Enums leave as strings, not numbers. A business application reading
/// <c>"Approved"</c> from a completion event needs no lookup table and does not
/// break when a value is inserted in the middle of the enum — which is exactly
/// the kind of change that looks harmless and silently reinterprets every
/// stored decision.
/// </para>
/// </summary>
public static class InstanceMapper
{
    public static WorkflowInstanceDto ToDto(WorkflowInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return new WorkflowInstanceDto(
            instance.Id,
            instance.ApplicationCode,
            instance.DefinitionCode,
            instance.DefinitionVersion,
            instance.ResourceType,
            instance.ResourceId,
            instance.RequestedBy,
            instance.CurrentStepKey,
            instance.Status.ToString(),
            instance.StartedAt,
            instance.CompletedAt,
            [.. instance.Actions
                .OrderBy(a => a.OccurredAt)
                .Select(a => new WorkflowActionDto(
                    a.StepKey, a.Action.ToString(), a.ActorUserId, a.Comment, a.OccurredAt))]);
    }
}

/// <summary>Turns a definition into what callers see.</summary>
public static class DefinitionMapper
{
    public static WorkflowDefinitionDto ToDto(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new WorkflowDefinitionDto(
            definition.Id,
            definition.ApplicationCode,
            definition.Code,
            definition.Version,
            definition.NameAr,
            definition.NameEn,
            definition.Description,
            definition.Status.ToString(),
            definition.InitialStepKey,
            [.. definition.Steps.OrderBy(s => s.Order).Select(ToDto)]);
    }

    public static WorkflowStepDto ToDto(WorkflowStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        return new WorkflowStepDto(
            step.Key,
            step.NameAr,
            step.NameEn,
            step.Order,
            step.Assignee.Strategy.ToString(),
            step.Assignee.UserId,
            step.Assignee.RoleId,
            step.Assignee.PositionId,
            step.Assignee.UnitId,

            // Hours rather than a duration string. Every client can read a
            // number; not every one parses ISO-8601 durations the same way, and
            // a service level misread by a factor of sixty is an escalation
            // storm or an escalation that never fires.
            step.ServiceLevel?.TotalHours,
            [.. step.Transitions.Select(t =>
                new WorkflowTransitionDto(t.Action.ToString(), t.TargetStepKey))]);
    }
}

/// <summary>Turns a task into an inbox row.</summary>
public static class TaskMapper
{
    public static WorkflowTaskDto ToDto(
        WorkflowTask task,
        WorkflowInstance instance,
        WorkflowStep? step,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(instance);

        return new WorkflowTaskDto(
            task.Id,
            task.InstanceId,
            instance.ApplicationCode,
            instance.DefinitionCode,
            instance.ResourceType,
            instance.ResourceId,
            task.StepKey,
            step?.NameAr ?? task.StepKey,
            step?.NameEn ?? task.StepKey,
            task.AssignedToUserId,
            task.DelegatedFromUserId,
            task.Status.ToString(),
            task.AssignedAt,
            task.DueAt,
            task.IsOverdue(now),

            // What this person can actually do here, from the definition this
            // instance is pinned to. Sent with the row so the screen offers the
            // real buttons rather than every verb the engine knows — a "Reject"
            // that the process does not permit is a refusal waiting to happen.
            [.. (step?.Transitions ?? []).Select(t => t.Action.ToString())
                .Concat([nameof(WorkflowActionType.Comment), nameof(WorkflowActionType.Delegate)])
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)]);
    }
}
