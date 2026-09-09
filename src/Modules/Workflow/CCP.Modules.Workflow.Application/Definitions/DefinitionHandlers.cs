using CCP.Kernel.Application.Auditing;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Workflow.Application.Abstractions;
using CCP.Modules.Workflow.Application.Engine;
using CCP.Modules.Workflow.Contracts.Dtos;
using CCP.Modules.Workflow.Domain;
using CCP.Modules.Workflow.Domain.Definitions;

namespace CCP.Modules.Workflow.Application.Definitions;

/// <summary>One step, as an application submits it.</summary>
public sealed record StepSpecification(
    string Key,
    string NameAr,
    string NameEn,
    int Order,
    AssigneeStrategy AssigneeStrategy,
    Guid? AssigneeUserId,
    Guid? AssigneeRoleId,
    Guid? AssigneePositionId,
    Guid? AssigneeUnitId,
    double? ServiceLevelHours,
    IReadOnlyList<TransitionSpecification> Transitions);

/// <summary>One allowed move, as an application submits it.</summary>
public sealed record TransitionSpecification(WorkflowActionType Action, string? TargetStepKey);

/// <summary>
/// Registering a process.
/// <para>
/// The whole definition arrives in one call and is published in the same one.
/// A half-registered process is not useful to anybody, and leaving it as a draft
/// to be assembled over several requests would mean the API had to answer "which
/// half of my process is on the server?" — a question with no good answer when
/// two clients are registering at once.
/// </para>
/// </summary>
public sealed record RegisterDefinitionCommand(
    string ApplicationCode,
    string Code,
    int Version,
    string NameAr,
    string NameEn,
    string? Description,
    string? InitialStepKey,
    IReadOnlyList<StepSpecification> Steps);

/// <summary>Closing a version to new instances.</summary>
public sealed record RetireDefinitionCommand(Guid DefinitionId);

/// <summary>Listing what is registered.</summary>
public sealed record GetDefinitionsQuery(string? ApplicationCode, bool IncludeRetired);

/// <summary>
/// Builds a definition, validates it, and freezes it.
/// <para>
/// <b>Versions are explicit, not inferred.</b> The caller says which version it
/// is registering and a repeat is refused. Auto-incrementing would mean two
/// deployments racing to register "the next version" and quietly producing two
/// different processes with consecutive numbers, neither of which is the one
/// anybody reviewed.
/// </para>
/// </summary>
public sealed class RegisterDefinitionHandler(
    IWorkflowRepository repository,
    IWorkflowUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IClock clock)
{
    public async Task<Result<WorkflowDefinitionDto>> HandleAsync(
        RegisterDefinitionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Steps.Count == 0)
        {
            return Result.Failure<WorkflowDefinitionDto>(WorkflowErrors.DefinitionHasNoSteps);
        }

        if (await repository.DefinitionVersionExistsAsync(
                command.ApplicationCode, command.Code, command.Version, cancellationToken))
        {
            return Result.Failure<WorkflowDefinitionDto>(WorkflowErrors.DefinitionVersionExists);
        }

        DateTimeOffset now = clock.UtcNow;

        Result<WorkflowDefinition> definition = WorkflowDefinition.Create(
            command.ApplicationCode, command.Code, command.Version,
            command.NameAr, command.NameEn, command.Description, now);

        if (definition.IsFailure)
        {
            return Result.Failure<WorkflowDefinitionDto>(definition.Errors);
        }

        foreach (StepSpecification specification in command.Steps)
        {
            Result<AssigneeRule> rule = BuildAssigneeRule(specification);

            if (rule.IsFailure)
            {
                return Result.Failure<WorkflowDefinitionDto>(rule.Errors);
            }

            Result<WorkflowStep> step = WorkflowStep.Create(
                definition.Value.Id,
                specification.Key,
                specification.NameAr,
                specification.NameEn,
                specification.Order,
                rule.Value,
                specification.ServiceLevelHours is { } hours
                    ? TimeSpan.FromHours(hours)
                    : null,
                now);

            if (step.IsFailure)
            {
                return Result.Failure<WorkflowDefinitionDto>(step.Errors);
            }

            foreach (TransitionSpecification transition in specification.Transitions)
            {
                Result added = step.Value.AddTransition(
                    WorkflowTransition.Create(
                        step.Value.Id, transition.Action, transition.TargetStepKey, now));

                if (added.IsFailure)
                {
                    return Result.Failure<WorkflowDefinitionDto>(added.Errors);
                }
            }

            Result appended = definition.Value.AddStep(step.Value, now);

            if (appended.IsFailure)
            {
                return Result.Failure<WorkflowDefinitionDto>(appended.Errors);
            }
        }

        if (command.InitialStepKey is { Length: > 0 } initial)
        {
            Result chosen = definition.Value.SetInitialStep(initial, now);

            if (chosen.IsFailure)
            {
                return Result.Failure<WorkflowDefinitionDto>(chosen.Errors);
            }
        }

        // Everything is checked here: targets resolve, every step is reachable.
        // A process that dead-ends is refused at registration rather than
        // discovered by whoever is waiting on step three.
        Result published = definition.Value.Publish(now);

        if (published.IsFailure)
        {
            return Result.Failure<WorkflowDefinitionDto>(published.Errors);
        }

        repository.AddDefinition(definition.Value);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                WorkflowAudit.ModuleName,
                "definition.registered",
                AuditOutcome.Success,
                "workflow-definition",
                definition.Value.Id.ToString(),
                NewValue: $$"""{"application":"{{definition.Value.ApplicationCode}}","code":"{{definition.Value.Code}}","version":{{definition.Value.Version}}}"""),
            cancellationToken);

        return Result.Success(DefinitionMapper.ToDto(definition.Value));
    }

    private static Result<AssigneeRule> BuildAssigneeRule(StepSpecification specification)
        => specification.AssigneeStrategy switch
        {
            AssigneeStrategy.User =>
                AssigneeRule.ForUser(specification.AssigneeUserId ?? Guid.Empty),

            AssigneeStrategy.Role =>
                AssigneeRule.ForRole(specification.AssigneeRoleId ?? Guid.Empty),

            AssigneeStrategy.Position =>
                AssigneeRule.ForPosition(specification.AssigneePositionId ?? Guid.Empty),

            AssigneeStrategy.RequesterManager =>
                Result.Success(AssigneeRule.ForRequesterManager()),

            AssigneeStrategy.UnitHead =>
                AssigneeRule.ForUnitHead(specification.AssigneeUnitId ?? Guid.Empty),

            AssigneeStrategy.SuppliedByCaller =>
                Result.Success(AssigneeRule.ForSuppliedList()),

            _ => Result.Failure<AssigneeRule>(WorkflowErrors.AssigneeTargetRequired)
        };
}

/// <summary>Closes a version to new instances, leaving running ones alone.</summary>
public sealed class RetireDefinitionHandler(
    IWorkflowRepository repository,
    IWorkflowUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        RetireDefinitionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        WorkflowDefinition? definition = await repository.FindDefinitionAsync(
            command.DefinitionId, cancellationToken);

        if (definition is null)
        {
            return Result.Failure(WorkflowErrors.DefinitionNotFound);
        }

        Result retired = definition.Retire(clock.UtcNow);

        if (retired.IsFailure)
        {
            return retired;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Lists the registered processes.</summary>
public sealed class GetDefinitionsHandler(IWorkflowRepository repository)
{
    public async Task<Result<IReadOnlyList<WorkflowDefinitionDto>>> HandleAsync(
        GetDefinitionsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<WorkflowDefinition> definitions = await repository.GetDefinitionsAsync(
            query.ApplicationCode, query.IncludeRetired, cancellationToken);

        return Result.Success<IReadOnlyList<WorkflowDefinitionDto>>(
            [.. definitions.Select(DefinitionMapper.ToDto)]);
    }
}
