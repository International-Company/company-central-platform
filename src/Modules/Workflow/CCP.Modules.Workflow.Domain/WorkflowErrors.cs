using CCP.Kernel.Results;
using CCP.Modules.Workflow.Domain.Definitions;

namespace CCP.Modules.Workflow.Domain;

/// <summary>
/// Every way the workflow engine can refuse, said in words a person can act on.
/// <para>
/// Named constants rather than strings at the throw site, so the same refusal
/// reads the same wherever it comes from and a client can branch on the code
/// rather than on prose.
/// </para>
/// </summary>
public static class WorkflowErrors
{
    // --- Definitions --------------------------------------------------------

    public static readonly Error ApplicationCodeRequired = Error.Validation(
        "WORKFLOW.APPLICATION_CODE_REQUIRED",
        "A definition must say which application owns it.",
        "applicationCode");

    public static readonly Error DefinitionCodeRequired = Error.Validation(
        "WORKFLOW.DEFINITION_CODE_REQUIRED", "A definition needs a code.", "code");

    public static readonly Error CodeLength = Error.Validation(
        "WORKFLOW.CODE_LENGTH", "A code must be between 2 and 64 characters.", "code");

    public static readonly Error CodeCharacters = Error.Validation(
        "WORKFLOW.CODE_CHARACTERS",
        "A code may contain letters, digits, hyphens, underscores and dots.",
        "code");

    public static readonly Error DefinitionNameRequired = Error.Validation(
        "WORKFLOW.DEFINITION_NAME_REQUIRED",
        "A definition needs a name in both languages.",
        "nameAr");

    public static readonly Error VersionMustBePositive = Error.Validation(
        "WORKFLOW.VERSION_INVALID", "A version number starts at 1.", "version");

    public static readonly Error DefinitionNotFound = Error.NotFound(
        "WORKFLOW.DEFINITION_NOT_FOUND", "The workflow definition does not exist.");

    public static readonly Error DefinitionNotDraft = Error.Rule(
        "WORKFLOW.DEFINITION_NOT_DRAFT",
        "A published definition cannot be changed. Publish a new version instead — "
        + "instances are running on this one.");

    public static readonly Error DefinitionNotPublished = Error.Rule(
        "WORKFLOW.DEFINITION_NOT_PUBLISHED",
        "Only a published definition can be started or retired.");

    public static readonly Error DefinitionHasNoSteps = Error.Rule(
        "WORKFLOW.DEFINITION_HAS_NO_STEPS", "A definition with no steps cannot be published.");

    public static readonly Error DefinitionHasNoInitialStep = Error.Rule(
        "WORKFLOW.DEFINITION_HAS_NO_INITIAL_STEP",
        "A definition must say which step an instance begins on.");

    public static readonly Error DefinitionVersionExists = Error.Conflict(
        "WORKFLOW.DEFINITION_VERSION_EXISTS",
        "That version of this definition already exists.");

    // --- Steps and transitions ---------------------------------------------

    public static readonly Error StepKeyRequired = Error.Validation(
        "WORKFLOW.STEP_KEY_REQUIRED", "A step needs a key.", "key");

    public static readonly Error StepNameRequired = Error.Validation(
        "WORKFLOW.STEP_NAME_REQUIRED", "A step needs a name in both languages.", "nameAr");

    public static readonly Error ServiceLevelMustBePositive = Error.Validation(
        "WORKFLOW.SERVICE_LEVEL_INVALID",
        "A service level must be longer than zero.",
        "serviceLevelHours");

    public static Error DuplicateStepKey(string key) => Error.Conflict(
        "WORKFLOW.DUPLICATE_STEP_KEY", $"The step '{key}' is defined more than once.");

    public static Error StepNotFound(string key) => Error.NotFound(
        "WORKFLOW.STEP_NOT_FOUND", $"The step '{key}' does not exist in this definition.");

    public static Error TransitionTargetNotFound(string from, string target) => Error.Rule(
        "WORKFLOW.TRANSITION_TARGET_NOT_FOUND",
        $"The step '{from}' moves to '{target}', which does not exist.");

    public static Error StepUnreachable(string key) => Error.Rule(
        "WORKFLOW.STEP_UNREACHABLE",
        $"No path reaches the step '{key}' from the initial step.");

    public static Error DuplicateTransition(string step, WorkflowActionType action) => Error.Conflict(
        "WORKFLOW.DUPLICATE_TRANSITION",
        $"The step '{step}' already says where '{action}' leads.");

    // --- Assignees ----------------------------------------------------------

    public static readonly Error AssigneeTargetRequired = Error.Validation(
        "WORKFLOW.ASSIGNEE_TARGET_REQUIRED",
        "This assignment strategy needs something to assign to.",
        "assignee");

    public static readonly Error AssigneeRequired = Error.Validation(
        "WORKFLOW.ASSIGNEE_REQUIRED", "A task needs somebody to do it.", "assignedTo");

    public static readonly Error NoAssigneesResolved = Error.Rule(
        "WORKFLOW.NO_ASSIGNEES_RESOLVED",
        "Nobody could be found for this step, so the request would wait forever. "
        + "Check that the role, position or manager it names still exists.");

    public static readonly Error SuppliedAssigneesRequired = Error.Validation(
        "WORKFLOW.SUPPLIED_ASSIGNEES_REQUIRED",
        "This step expects the calling application to name its assignees.",
        "assignees");

    // --- Instances ----------------------------------------------------------

    public static readonly Error InstanceNotFound = Error.NotFound(
        "WORKFLOW.INSTANCE_NOT_FOUND", "The workflow instance does not exist.");

    public static readonly Error InstanceNotRunning = Error.Rule(
        "WORKFLOW.INSTANCE_NOT_RUNNING", "This request has already finished.");

    public static readonly Error ResourceRequired = Error.Validation(
        "WORKFLOW.RESOURCE_REQUIRED",
        "An instance must say what is being approved.",
        "resourceType");

    public static readonly Error RequesterRequired = Error.Validation(
        "WORKFLOW.REQUESTER_REQUIRED", "An instance must have a requester.", "requestedBy");

    public static Error StepIsNotCurrent(string key) => Error.Rule(
        "WORKFLOW.STEP_NOT_CURRENT",
        $"This request is no longer at the step '{key}'. Somebody may have acted already.");

    public static Error ActionNotPermitted(string step, WorkflowActionType action) => Error.Rule(
        "WORKFLOW.ACTION_NOT_PERMITTED",
        $"'{action}' is not one of the things that can be done at the step '{step}'.");

    public static Error ReturnNeedsATarget(string step) => Error.Rule(
        "WORKFLOW.RETURN_NEEDS_A_TARGET",
        $"The step '{step}' allows a return but does not say where to. Returning means "
        + "sending the request back to be corrected, so it must name a step to go back to.");

    // --- Tasks --------------------------------------------------------------

    public static readonly Error TaskNotFound = Error.NotFound(
        "WORKFLOW.TASK_NOT_FOUND", "The task does not exist.");

    public static readonly Error ReasonRequired = Error.Validation(
        "WORKFLOW.REASON_REQUIRED",
        "Say why. Six months later this is the only part of the record that explains itself.",
        "reason");

    public static readonly Error TaskNotPending = Error.Rule(
        "WORKFLOW.TASK_NOT_PENDING", "This task has already been settled.");

    public static readonly Error NotTheAssignee = Error.Forbidden(
        "WORKFLOW.NOT_THE_ASSIGNEE", "This task is not assigned to you.");

    public static readonly Error InvalidDelegate = Error.Validation(
        "WORKFLOW.INVALID_DELEGATE",
        "A task must be delegated to somebody else.",
        "delegateToUserId");

    public static readonly Error InvalidReassignment = Error.Validation(
        "WORKFLOW.INVALID_REASSIGNMENT",
        "Reassign the task to somebody other than the person who already holds it.",
        "assigneeUserId");

    public static readonly Error TaskAlreadyEscalated = Error.Rule(
        "WORKFLOW.TASK_ALREADY_ESCALATED", "This task has already been escalated.");
}
