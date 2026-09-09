using CCP.Modules.Workflow.Application.Abstractions;
using CCP.Modules.Workflow.Domain.Definitions;
using CCP.Modules.Workflow.Domain.Instances;
using Microsoft.EntityFrameworkCore;

namespace CCP.Modules.Workflow.Infrastructure.Persistence;

/// <summary>
/// Reads and writes the workflow schema.
/// <para>
/// Definitions come back with their steps and transitions attached, because
/// there is no useful thing to do with a definition that does not involve
/// walking it — validating a transition, resolving an assignee, deciding where
/// an approval goes next. Loading them separately would mean three round trips
/// for every action anybody takes.
/// </para>
/// </summary>
public sealed class WorkflowRepository(WorkflowDbContext dbContext) : IWorkflowRepository
{
    // --- Definitions --------------------------------------------------------

    public async Task<WorkflowDefinition?> FindDefinitionAsync(
        Guid definitionId, CancellationToken cancellationToken = default)
        => await dbContext.Definitions
            .FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);

    public async Task<WorkflowDefinition?> FindLatestPublishedAsync(
        string applicationCode, string code, CancellationToken cancellationToken = default)
    {
        string application = applicationCode.Trim().ToLowerInvariant();
        string definition = code.Trim().ToLowerInvariant();

        // Highest published version. Retired versions are excluded here and
        // only here: an instance already running on a retired version keeps
        // running, and finds it through FindVersionAsync.
        return await dbContext.Definitions
            .Where(d => d.ApplicationCode == application
                     && d.Code == definition
                     && d.Status == DefinitionStatus.Published)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<WorkflowDefinition?> FindVersionAsync(
        string applicationCode, string code, int version,
        CancellationToken cancellationToken = default)
    {
        string application = applicationCode.Trim().ToLowerInvariant();
        string definition = code.Trim().ToLowerInvariant();

        // No status filter, deliberately. This is how a running instance finds
        // the version it is pinned to, and that version may well have been
        // retired since — retiring stops new instances, not existing ones.
        return await dbContext.Definitions
            .FirstOrDefaultAsync(
                d => d.ApplicationCode == application
                  && d.Code == definition
                  && d.Version == version,
                cancellationToken);
    }

    public async Task<bool> DefinitionVersionExistsAsync(
        string applicationCode, string code, int version,
        CancellationToken cancellationToken = default)
    {
        string application = applicationCode.Trim().ToLowerInvariant();
        string definition = code.Trim().ToLowerInvariant();

        return await dbContext.Definitions.AnyAsync(
            d => d.ApplicationCode == application
              && d.Code == definition
              && d.Version == version,
            cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowDefinition>> GetDefinitionsAsync(
        string? applicationCode, bool includeRetired,
        CancellationToken cancellationToken = default)
    {
        IQueryable<WorkflowDefinition> query = dbContext.Definitions.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(applicationCode))
        {
            string application = applicationCode.Trim().ToLowerInvariant();

            query = query.Where(d => d.ApplicationCode == application);
        }

        if (!includeRetired)
        {
            query = query.Where(d => d.Status != DefinitionStatus.Retired);
        }

        return await query
            .OrderBy(d => d.ApplicationCode)
            .ThenBy(d => d.Code)
            .ThenByDescending(d => d.Version)
            .ToListAsync(cancellationToken);
    }

    public void AddDefinition(WorkflowDefinition definition)
        => dbContext.Definitions.Add(definition);

    // --- Instances ----------------------------------------------------------

    public async Task<WorkflowInstance?> FindInstanceAsync(
        Guid instanceId, CancellationToken cancellationToken = default)
        => await dbContext.Instances
            .Include(i => i.Actions)
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

    public async Task<(IReadOnlyList<WorkflowInstance> Items, long Total)> SearchInstancesAsync(
        string? applicationCode,
        string? resourceType,
        string? resourceId,
        InstanceStatus? status,
        Guid? requestedBy,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        IQueryable<WorkflowInstance> query = dbContext.Instances.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(applicationCode))
        {
            string application = applicationCode.Trim().ToLowerInvariant();

            query = query.Where(i => i.ApplicationCode == application);
        }

        if (!string.IsNullOrWhiteSpace(resourceType))
        {
            query = query.Where(i => i.ResourceType == resourceType);
        }

        if (!string.IsNullOrWhiteSpace(resourceId))
        {
            query = query.Where(i => i.ResourceId == resourceId);
        }

        if (status is { } wanted)
        {
            query = query.Where(i => i.Status == wanted);
        }

        if (requestedBy is { } requester)
        {
            query = query.Where(i => i.RequestedBy == requester);
        }

        long total = await query.LongCountAsync(cancellationToken);

        List<WorkflowInstance> items = await query
            .OrderByDescending(i => i.StartedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public void AddInstance(WorkflowInstance instance)
        => dbContext.Instances.Add(instance);

    // --- Tasks --------------------------------------------------------------

    public async Task<WorkflowTask?> FindTaskAsync(
        Guid taskId, CancellationToken cancellationToken = default)
        => await dbContext.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);

    public async Task<IReadOnlyList<WorkflowTask>> GetPendingTasksAsync(
        Guid instanceId, CancellationToken cancellationToken = default)
        => await dbContext.Tasks
            .Where(t => t.InstanceId == instanceId && t.Status == Domain.Instances.TaskStatus.Pending)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<WorkflowTask> Items, long Total)> GetTasksForUserAsync(
        Guid userId, bool pendingOnly, int skip, int take,
        CancellationToken cancellationToken = default)
    {
        IQueryable<WorkflowTask> query = dbContext.Tasks
            .AsNoTracking()
            .Where(t => t.AssignedToUserId == userId);

        if (pendingOnly)
        {
            query = query.Where(t => t.Status == Domain.Instances.TaskStatus.Pending);
        }

        long total = await query.LongCountAsync(cancellationToken);

        // Oldest first. An inbox sorted newest-first buries the thing that has
        // been waiting longest, which is the one most likely to be late.
        List<WorkflowTask> items = await query
            .OrderBy(t => t.DueAt == null)
            .ThenBy(t => t.DueAt)
            .ThenBy(t => t.AssignedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<IReadOnlyList<WorkflowTask>> GetOverdueTasksAsync(
        DateTimeOffset now, int limit, CancellationToken cancellationToken = default)
        => await dbContext.Tasks
            .Where(t => t.Status == Domain.Instances.TaskStatus.Pending
                     && t.DueAt != null
                     && t.DueAt < now
                     && t.EscalatedAt == null)
            .OrderBy(t => t.DueAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public void AddTask(WorkflowTask task) => dbContext.Tasks.Add(task);
}
