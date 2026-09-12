using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;

namespace CCP.Modules.Workflow.Domain.Definitions;

/// <summary>
/// A named approval process, as data.
/// <para>
/// <b>The engine understands states, transitions, assignees, actions and
/// timers. It does not understand what is being approved</b> (ARCHITECTURE.md
/// §16.1). There is no threshold here, no amount, no eligibility rule and no
/// "if the value exceeds X". Every one of those belongs to the application that
/// starts the instance, and the boundary is what makes this module reusable by
/// systems nobody has designed yet (P4).
/// </para>
/// <para>
/// <b>Versioned, and versions are immutable once published.</b> An instance
/// records the version it started on and runs to completion on that version, so
/// changing a definition never rewrites a decision somebody already took. That
/// is not a convenience: an approval whose rules changed underneath it is an
/// approval nobody can account for afterwards.
/// </para>
/// <para>
/// Registered by applications through the API, which is what lets a new
/// approval process arrive with no Platform code change at all.
/// </para>
/// </summary>
public sealed class WorkflowDefinition : AggregateRoot, IAuditableEntity
{
    private readonly List<WorkflowStep> _steps = [];

    private WorkflowDefinition() { }

    private WorkflowDefinition(
        Guid id,
        string applicationCode,
        string code,
        int version,
        string nameAr,
        string nameEn,
        DateTimeOffset now)
        : base(id)
    {
        ApplicationCode = applicationCode;
        Code = code;
        Version = version;
        NameAr = nameAr;
        NameEn = nameEn;
        Status = DefinitionStatus.Draft;
        CreatedAt = now;
    }

    /// <summary>
    /// Which application owns this process. Namespaced so two systems can both
    /// have a "purchase-approval" without colliding.
    /// </summary>
    public string ApplicationCode { get; private set; } = string.Empty;

    /// <summary>Stable within the application, across every version.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Increments per published version of the same code.</summary>
    public int Version { get; private set; }

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public DefinitionStatus Status { get; private set; }

    /// <summary>The step an instance begins on.</summary>
    public string? InitialStepKey { get; private set; }

    public IReadOnlyList<WorkflowStep> Steps => _steps.AsReadOnly();

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static Result<WorkflowDefinition> Create(
        string applicationCode,
        string code,
        int version,
        string nameAr,
        string nameEn,
        string? description,
        DateTimeOffset now)
    {
        Result validation = ValidateCode(applicationCode, WorkflowErrors.ApplicationCodeRequired)
            .Combine(ValidateCode(code, WorkflowErrors.DefinitionCodeRequired))
            .Combine(string.IsNullOrWhiteSpace(nameAr)
                ? Result.Failure(WorkflowErrors.DefinitionNameRequired)
                : Result.Success())
            .Combine(string.IsNullOrWhiteSpace(nameEn)
                ? Result.Failure(WorkflowErrors.DefinitionNameRequired)
                : Result.Success());

        if (validation.IsFailure)
        {
            return Result.Failure<WorkflowDefinition>(validation.Errors);
        }

        if (version < 1)
        {
            return Result.Failure<WorkflowDefinition>(WorkflowErrors.VersionMustBePositive);
        }

        return Result.Success(new WorkflowDefinition(
            Uuid7.NewGuid(now),
            applicationCode.Trim().ToLowerInvariant(),
            code.Trim().ToLowerInvariant(),
            version,
            nameAr.Trim(),
            nameEn.Trim(),
            now)
        {
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim()
        });
    }

    /// <summary>
    /// Adds a step. Only while the definition is a draft.
    /// <para>
    /// A published definition cannot gain a step, because instances are running
    /// on it and a step appearing mid-flight is a transition nobody validated.
    /// </para>
    /// </summary>
    public Result AddStep(WorkflowStep step, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(step);

        if (Status != DefinitionStatus.Draft)
        {
            return Result.Failure(WorkflowErrors.DefinitionNotDraft);
        }

        if (_steps.Any(s => string.Equals(s.Key, step.Key, StringComparison.Ordinal)))
        {
            return Result.Failure(WorkflowErrors.DuplicateStepKey(step.Key));
        }

        _steps.Add(step);
        UpdatedAt = now;

        // The first step added is the entry point unless one is chosen later.
        InitialStepKey ??= step.Key;

        return Result.Success();
    }

    /// <summary>Chooses which step an instance begins on.</summary>
    public Result SetInitialStep(string stepKey, DateTimeOffset now)
    {
        if (Status != DefinitionStatus.Draft)
        {
            return Result.Failure(WorkflowErrors.DefinitionNotDraft);
        }

        if (!_steps.Any(s => string.Equals(s.Key, stepKey, StringComparison.Ordinal)))
        {
            return Result.Failure(WorkflowErrors.StepNotFound(stepKey));
        }

        InitialStepKey = stepKey;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Freezes the definition so instances may run on it.
    /// <para>
    /// <b>Validation happens here, once, rather than at every transition.</b>
    /// A definition that names a target step which does not exist is a process
    /// that will dead-end halfway through somebody's approval — and finding that
    /// out at the moment it happens means finding it out in production, to the
    /// person waiting. So every transition target is resolved now, and a
    /// definition that cannot be walked is refused publication.
    /// </para>
    /// </summary>
    public Result Publish(DateTimeOffset now)
    {
        if (Status != DefinitionStatus.Draft)
        {
            return Result.Failure(WorkflowErrors.DefinitionNotDraft);
        }

        if (_steps.Count == 0)
        {
            return Result.Failure(WorkflowErrors.DefinitionHasNoSteps);
        }

        if (InitialStepKey is null)
        {
            return Result.Failure(WorkflowErrors.DefinitionHasNoInitialStep);
        }

        var keys = _steps.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);

        foreach (WorkflowStep step in _steps)
        {
            foreach (WorkflowTransition transition in step.Transitions)
            {
                // A null target is "this ends the process", which is how a
                // terminal step is expressed without a special step type.
                if (transition.TargetStepKey is { } target && !keys.Contains(target))
                {
                    return Result.Failure(WorkflowErrors.TransitionTargetNotFound(step.Key, target));
                }
            }
        }

        // Every step must be reachable from the entry point. An unreachable step
        // is either a mistake or a leftover, and both are worth refusing before
        // anyone relies on the process.
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>([InitialStepKey]);

        while (pending.Count > 0)
        {
            string current = pending.Dequeue();

            if (!reachable.Add(current))
            {
                continue;
            }

            WorkflowStep? step = _steps.FirstOrDefault(
                s => string.Equals(s.Key, current, StringComparison.Ordinal));

            foreach (WorkflowTransition transition in step?.Transitions ?? [])
            {
                if (transition.TargetStepKey is { } target)
                {
                    pending.Enqueue(target);
                }
            }
        }

        string? orphan = _steps
            .Select(s => s.Key)
            .FirstOrDefault(key => !reachable.Contains(key));

        if (orphan is not null)
        {
            return Result.Failure(WorkflowErrors.StepUnreachable(orphan));
        }

        // A step that asks the caller for its assignees can only be the first
        // one. The caller supplies them when it starts the instance; a step
        // reached later is reached by somebody acting, and the engine resolves
        // it with an empty supplied list on purpose -- that is what keeps
        // business-conditional routing outside the engine (ARCHITECTURE.md
        // 16.3). Such a step therefore resolves to nobody and the transition
        // into it fails.
        //
        // Refused here rather than there. "Nobody could be found for this step"
        // arriving three weeks later, to the person who asked for something, is
        // the same fact delivered at the worst possible moment: the definition
        // could not have worked on the day it was written.
        WorkflowStep? misplaced = _steps.FirstOrDefault(step =>
            step.Assignee.Strategy == AssigneeStrategy.SuppliedByCaller
            && !string.Equals(step.Key, InitialStepKey, StringComparison.Ordinal));

        if (misplaced is not null)
        {
            return Result.Failure(WorkflowErrors.SuppliedByCallerOnLaterStep(misplaced.Key));
        }

        Status = DefinitionStatus.Published;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Stops new instances starting on this version.
    /// <para>
    /// Running instances are untouched. Retiring a version is a statement about
    /// the future, not a way to cancel approvals already in progress.
    /// </para>
    /// </summary>
    public Result Retire(DateTimeOffset now)
    {
        if (Status != DefinitionStatus.Published)
        {
            return Result.Failure(WorkflowErrors.DefinitionNotPublished);
        }

        Status = DefinitionStatus.Retired;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>The step with this key, or null.</summary>
    public WorkflowStep? FindStep(string key)
        => _steps.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.Ordinal));

    private static Result ValidateCode(string code, Error missing)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure(missing);
        }

        string trimmed = code.Trim();

        if (trimmed.Length is < 2 or > 64)
        {
            return Result.Failure(WorkflowErrors.CodeLength);
        }

        foreach (char c in trimmed)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.'))
            {
                return Result.Failure(WorkflowErrors.CodeCharacters);
            }
        }

        return Result.Success();
    }
}

/// <summary>Where a definition version is in its life.</summary>
public enum DefinitionStatus
{
    /// <summary>Being written. No instance may start on it.</summary>
    Draft = 1,

    /// <summary>Frozen and usable. Steps and transitions cannot change.</summary>
    Published = 2,

    /// <summary>No new instances. Running ones continue unaffected.</summary>
    Retired = 3
}
