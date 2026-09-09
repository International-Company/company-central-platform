namespace CCP.Modules.Workflow.Contracts.Dtos;

/// <summary>A process, as callers see it.</summary>
public sealed record WorkflowDefinitionDto(
    Guid Id,
    string ApplicationCode,
    string Code,
    int Version,
    string NameAr,
    string NameEn,
    string? Description,
    string Status,
    string? InitialStepKey,
    IReadOnlyList<WorkflowStepDto> Steps);

/// <summary>One state in a process.</summary>
public sealed record WorkflowStepDto(
    string Key,
    string NameAr,
    string NameEn,
    int Order,
    string AssigneeStrategy,
    Guid? AssigneeUserId,
    Guid? AssigneeRoleId,
    Guid? AssigneePositionId,
    Guid? AssigneeUnitId,
    double? ServiceLevelHours,
    IReadOnlyList<WorkflowTransitionDto> Transitions);

/// <summary>An allowed move. A null target ends the instance.</summary>
public sealed record WorkflowTransitionDto(string Action, string? TargetStepKey);

/// <summary>
/// One run of a process.
/// <para>
/// The resource is a type and an id and nothing more. The Platform records that
/// something was approved; the calling application is the only thing that knows
/// what it was.
/// </para>
/// </summary>
public sealed record WorkflowInstanceDto(
    Guid Id,
    string ApplicationCode,
    string DefinitionCode,
    int DefinitionVersion,
    string ResourceType,
    string ResourceId,
    Guid RequestedBy,
    string? CurrentStepKey,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<WorkflowActionDto> Actions);

/// <summary>Something that happened to an instance.</summary>
public sealed record WorkflowActionDto(
    string StepKey,
    string Action,
    Guid ActorUserId,
    string? Comment,
    DateTimeOffset OccurredAt);

/// <summary>
/// Something waiting for one person.
/// <para>
/// Carries the instance's resource so an inbox can say what each item is about
/// without a second call per row — which on a screen of twenty tasks is twenty
/// round trips to render one list.
/// </para>
/// </summary>
public sealed record WorkflowTaskDto(
    Guid Id,
    Guid InstanceId,
    string ApplicationCode,
    string DefinitionCode,
    string ResourceType,
    string ResourceId,
    string StepKey,
    string StepNameAr,
    string StepNameEn,
    Guid AssignedToUserId,
    Guid? DelegatedFromUserId,
    string Status,
    DateTimeOffset AssignedAt,
    DateTimeOffset? DueAt,
    bool IsOverdue,
    IReadOnlyList<string> AllowedActions);
