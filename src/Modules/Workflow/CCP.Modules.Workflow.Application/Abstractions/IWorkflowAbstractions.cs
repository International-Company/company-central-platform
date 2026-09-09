using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Application.Events;
using CCP.Modules.Workflow.Domain.Definitions;
using CCP.Modules.Workflow.Domain.Instances;

namespace CCP.Modules.Workflow.Application.Abstractions;

/// <summary>Commits the module's changes as one transaction.</summary>
public interface IWorkflowUnitOfWork : IUnitOfWork;

/// <summary>The module's slice of the transactional outbox.</summary>
public interface IWorkflowOutbox : IOutbox;

/// <summary>
/// Turns a step's rule into the people who must act on it.
/// <para>
/// <b>Implemented outside this module, and that is the whole point.</b>
/// Resolving "the requester's manager" or "whoever holds this position" means
/// reading the organizational structure, and Workflow referencing Organization
/// would tie a reusable engine to one particular company model (§6.2). The
/// contract lives here; the answer comes from the composition root.
/// </para>
/// <para>
/// Returning nobody is a failure the caller must handle, not an empty list to
/// shrug at: a step with no assignee is a request that waits forever, and it
/// should be refused at the moment it would begin rather than discovered three
/// weeks later by the person still waiting.
/// </para>
/// </summary>
public interface IAssigneeResolver
{
    Task<IReadOnlyList<Guid>> ResolveAsync(
        AssigneeRule rule,
        Guid requesterUserId,
        IReadOnlyList<Guid> suppliedAssignees,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes the module's aggregates.</summary>
public interface IWorkflowRepository
{
    // --- Definitions --------------------------------------------------------

    Task<WorkflowDefinition?> FindDefinitionAsync(
        Guid definitionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The version an instance should start on: the highest published one for
    /// this application and code.
    /// </summary>
    Task<WorkflowDefinition?> FindLatestPublishedAsync(
        string applicationCode, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// The exact version an instance is running on.
    /// <para>
    /// Needed because an instance records a version and must be evaluated
    /// against that version for its whole life — never against whatever is
    /// current now.
    /// </para>
    /// </summary>
    Task<WorkflowDefinition?> FindVersionAsync(
        string applicationCode, string code, int version,
        CancellationToken cancellationToken = default);

    Task<bool> DefinitionVersionExistsAsync(
        string applicationCode, string code, int version,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowDefinition>> GetDefinitionsAsync(
        string? applicationCode, bool includeRetired,
        CancellationToken cancellationToken = default);

    void AddDefinition(WorkflowDefinition definition);

    // --- Instances ----------------------------------------------------------

    Task<WorkflowInstance?> FindInstanceAsync(
        Guid instanceId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<WorkflowInstance> Items, long Total)> SearchInstancesAsync(
        string? applicationCode,
        string? resourceType,
        string? resourceId,
        InstanceStatus? status,
        Guid? requestedBy,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    void AddInstance(WorkflowInstance instance);

    // --- Tasks --------------------------------------------------------------

    Task<WorkflowTask?> FindTaskAsync(Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>Every task still waiting on this instance, at any step.</summary>
    Task<IReadOnlyList<WorkflowTask>> GetPendingTasksAsync(
        Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>One person's inbox.</summary>
    Task<(IReadOnlyList<WorkflowTask> Items, long Total)> GetTasksForUserAsync(
        Guid userId, bool pendingOnly, int skip, int take,
        CancellationToken cancellationToken = default);

    /// <summary>Tasks past their service level and not yet escalated.</summary>
    Task<IReadOnlyList<WorkflowTask>> GetOverdueTasksAsync(
        DateTimeOffset now, int limit, CancellationToken cancellationToken = default);

    void AddTask(WorkflowTask task);
}
