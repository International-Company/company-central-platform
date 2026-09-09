using CCP.Kernel.Paging;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Workflow.Application.Abstractions;
using CCP.Modules.Workflow.Contracts.Dtos;
using CCP.Modules.Workflow.Domain;
using CCP.Modules.Workflow.Domain.Definitions;
using CCP.Modules.Workflow.Domain.Instances;

namespace CCP.Modules.Workflow.Application.Instances;

/// <summary>One person's inbox.</summary>
public sealed record GetMyTasksQuery(Guid UserId, bool PendingOnly, int Page, int PageSize);

/// <summary>One instance, with everything that happened to it.</summary>
public sealed record GetInstanceQuery(Guid InstanceId);

/// <summary>Finding instances, usually by the record they belong to.</summary>
public sealed record SearchInstancesQuery(
    string? ApplicationCode,
    string? ResourceType,
    string? ResourceId,
    InstanceStatus? Status,
    Guid? RequestedBy,
    int Page,
    int PageSize);

/// <summary>
/// The task inbox.
/// <para>
/// Each row carries what the task is about and what can be done to it, because
/// an inbox that needed a call per row to render would be twenty round trips for
/// one screen — and the definitions those answers come from are shared between
/// rows, so they are fetched once and reused.
/// </para>
/// </summary>
public sealed class GetMyTasksHandler(IWorkflowRepository repository, IClock clock)
{
    public async Task<Result<PagedResult<WorkflowTaskDto>>> HandleAsync(
        GetMyTasksQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int page = Math.Max(1, query.Page);
        int pageSize = Math.Clamp(query.PageSize, 1, 100);

        (IReadOnlyList<WorkflowTask> tasks, long total) = await repository.GetTasksForUserAsync(
            query.UserId, query.PendingOnly, (page - 1) * pageSize, pageSize, cancellationToken);

        DateTimeOffset now = clock.UtcNow;

        var instances = new Dictionary<Guid, WorkflowInstance>();
        var definitions = new Dictionary<(string, string, int), WorkflowDefinition?>();
        var rows = new List<WorkflowTaskDto>(tasks.Count);

        foreach (WorkflowTask task in tasks)
        {
            if (!instances.TryGetValue(task.InstanceId, out WorkflowInstance? instance))
            {
                instance = await repository.FindInstanceAsync(task.InstanceId, cancellationToken);

                if (instance is null)
                {
                    // A task whose instance is gone should not exist. Skipped
                    // rather than thrown: one broken row must not take the whole
                    // inbox down with it.
                    continue;
                }

                instances[task.InstanceId] = instance;
            }

            var key = (instance.ApplicationCode, instance.DefinitionCode, instance.DefinitionVersion);

            if (!definitions.TryGetValue(key, out WorkflowDefinition? definition))
            {
                definition = await repository.FindVersionAsync(
                    instance.ApplicationCode, instance.DefinitionCode,
                    instance.DefinitionVersion, cancellationToken);

                definitions[key] = definition;
            }

            rows.Add(TaskMapper.ToDto(
                task, instance, definition?.FindStep(task.StepKey), now));
        }

        return Result.Success(new PagedResult<WorkflowTaskDto>(rows, page, pageSize, total));
    }
}

/// <summary>One instance and its history.</summary>
public sealed class GetInstanceHandler(IWorkflowRepository repository)
{
    public async Task<Result<WorkflowInstanceDto>> HandleAsync(
        GetInstanceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        WorkflowInstance? instance = await repository.FindInstanceAsync(
            query.InstanceId, cancellationToken);

        return instance is null
            ? Result.Failure<WorkflowInstanceDto>(WorkflowErrors.InstanceNotFound)
            : Result.Success(InstanceMapper.ToDto(instance));
    }
}

/// <summary>
/// Finding instances.
/// <para>
/// The query a business application actually asks is "what is happening to my
/// record", so resource type and id are first-class filters rather than
/// something to page through and match by hand.
/// </para>
/// </summary>
public sealed class SearchInstancesHandler(IWorkflowRepository repository)
{
    public async Task<Result<PagedResult<WorkflowInstanceDto>>> HandleAsync(
        SearchInstancesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int page = Math.Max(1, query.Page);
        int pageSize = Math.Clamp(query.PageSize, 1, 100);

        (IReadOnlyList<WorkflowInstance> items, long total) = await repository.SearchInstancesAsync(
            query.ApplicationCode, query.ResourceType, query.ResourceId,
            query.Status, query.RequestedBy,
            (page - 1) * pageSize, pageSize, cancellationToken);

        return Result.Success(new PagedResult<WorkflowInstanceDto>(
            [.. items.Select(InstanceMapper.ToDto)], page, pageSize, total));
    }
}
