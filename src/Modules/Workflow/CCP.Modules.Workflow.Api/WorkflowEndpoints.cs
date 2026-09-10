using CCP.Kernel.Api.Context;
using CCP.Kernel.Api.Errors;
using CCP.Kernel.Api.Security;
using CCP.Kernel.Paging;
using CCP.Kernel.Results;
using CCP.Modules.Workflow.Application.Definitions;
using CCP.Modules.Workflow.Application.Instances;
using CCP.Modules.Workflow.Contracts.Dtos;
using CCP.Modules.Workflow.Domain.Definitions;
using CCP.Modules.Workflow.Domain.Instances;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;

namespace CCP.Modules.Workflow.Api;

/// <summary>
/// The workflow engine's surface.
/// <para>
/// Three audiences, and they are kept apart on purpose. <b>Applications</b>
/// register definitions and start instances. <b>People</b> read their own inbox
/// and act on their own tasks. <b>Administrators</b> look at what is running.
/// Each has its own permission, because "may configure processes" and "may
/// approve things" are different jobs that happen to touch the same module.
/// </para>
/// </summary>
public static class WorkflowEndpoints
{
    public static void MapWorkflowEndpoints(this IEndpointRouteBuilder versionGroup)
    {
        MapDefinitionEndpoints(versionGroup);
        MapInstanceEndpoints(versionGroup);
        MapTaskEndpoints(versionGroup);
    }

    private static void MapDefinitionEndpoints(IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder definitions = versionGroup
            .MapGroup("/workflow/definitions")
            .WithTags("Workflow");

        definitions.MapGet("/", async (
            string? applicationCode,
            bool? includeRetired,
            HttpContext context,
            [FromServices] GetDefinitionsHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<WorkflowDefinitionDto>> result = await handler.HandleAsync(
                new GetDefinitionsQuery(applicationCode, includeRetired ?? false),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.workflow.view"))
            .Produces<IReadOnlyList<WorkflowDefinitionDto>>(StatusCodes.Status200OK)
            .WithName("GetWorkflowDefinitions")
            .WithSummary("Lists the registered approval processes.");

        definitions.MapPost("/", async (
            RegisterDefinitionRequest request,
            HttpContext context,
            [FromServices] RegisterDefinitionHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result validation = request.Validate();

            if (validation.IsFailure)
            {
                return validation.ToHttpResult(context, requestContext);
            }

            Result<WorkflowDefinitionDto> result = await handler.HandleAsync(
                request.ToCommand(), cancellationToken);

            return result.IsSuccess
                ? result.ToCreatedResult(
                    $"/api/v1/workflow/definitions/{result.Value.Id}", context, requestContext)
                : result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.workflow.manage"))
            .Produces<WorkflowDefinitionDto>(StatusCodes.Status201Created)
            .WithName("RegisterWorkflowDefinition")
            .WithSummary("Registers and publishes a version of an approval process.");

        definitions.MapPost("/{id:guid}/retire", async (
            Guid id,
            HttpContext context,
            [FromServices] RetireDefinitionHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.HandleAsync(
                new RetireDefinitionCommand(id), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.workflow.manage"))
            .WithName("RetireWorkflowDefinition")
            .WithSummary("Stops new instances starting on a version. Running ones continue.");
    }

    private static void MapInstanceEndpoints(IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder instances = versionGroup
            .MapGroup("/workflow/instances")
            .WithTags("Workflow");

        instances.MapPost("/", async (
            StartInstanceRequest request,
            HttpContext context,
            [FromServices] StartInstanceHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result validation = request.Validate();

            if (validation.IsFailure)
            {
                return validation.ToHttpResult(context, requestContext);
            }

            // The requester comes from the token. An endpoint that let a caller
            // name whose request this is would let anybody file an approval in
            // somebody else's name — and the whole trail would then be a record
            // of a decision attributed to the wrong person.
            if (!CallerIdentity.TryGetUserId(context.User, out Guid requestedBy))
            {
                return Results.Unauthorized();
            }

            Result<WorkflowInstanceDto> result = await handler.HandleAsync(
                new StartInstanceCommand(
                    request.ApplicationCode,
                    request.DefinitionCode,
                    request.ResourceType,
                    request.ResourceId,
                    requestedBy,
                    request.Assignees ?? []),
                cancellationToken);

            return result.IsSuccess
                ? result.ToCreatedResult(
                    $"/api/v1/workflow/instances/{result.Value.Id}", context, requestContext)
                : result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.workflow.start"))
            .Produces<WorkflowInstanceDto>(StatusCodes.Status201Created)
            .WithName("StartWorkflowInstance")
            .WithSummary("Starts an approval against a business record.");

        instances.MapGet("/", async (
            string? applicationCode,
            string? resourceType,
            string? resourceId,
            string? status,
            int? page,
            int? pageSize,
            HttpContext context,
            [FromServices] SearchInstancesHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            InstanceStatus? wanted = null;

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse(status, ignoreCase: true, out InstanceStatus parsed))
                {
                    return Result.Failure(Error.Validation(
                        "WORKFLOW.UNKNOWN_STATUS",
                        "That is not a workflow status.",
                        "status"))
                        .ToHttpResult(context, requestContext);
                }

                wanted = parsed;
            }

            Result<PagedResult<WorkflowInstanceDto>> result = await handler.HandleAsync(
                new SearchInstancesQuery(
                    applicationCode, resourceType, resourceId, wanted, null,
                    page ?? 1, pageSize ?? 25),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.workflow.view"))
            .Produces<PagedResult<WorkflowInstanceDto>>(StatusCodes.Status200OK)
            .WithName("SearchWorkflowInstances")
            .WithSummary("Finds approvals, usually by the record they belong to.");

        instances.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext context,
            [FromServices] GetInstanceHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<WorkflowInstanceDto> result = await handler.HandleAsync(
                new GetInstanceQuery(id), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.workflow.view"))
            .Produces<WorkflowInstanceDto>(StatusCodes.Status200OK)
            .WithName("GetWorkflowInstance")
            .WithSummary("Returns one approval with everything that happened to it.");

        instances.MapPost("/{id:guid}/cancel", async (
            Guid id,
            CancelInstanceRequest request,
            HttpContext context,
            [FromServices] CancelInstanceHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            if (!CallerIdentity.TryGetUserId(context.User, out Guid actorUserId))
            {
                return Results.Unauthorized();
            }

            Result result = await handler.HandleAsync(
                new CancelInstanceCommand(id, actorUserId, request.Reason), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.workflow.start"))
            .WithName("CancelWorkflowInstance")
            .WithSummary("Withdraws an approval and closes every task on it.");
    }

    private static void MapTaskEndpoints(IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder tasks = versionGroup
            .MapGroup("/me/tasks")
            .WithTags("Workflow");

        tasks.MapGet("/", async (
            bool? includeCompleted,
            int? page,
            int? pageSize,
            HttpContext context,
            [FromServices] GetMyTasksHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            if (!CallerIdentity.TryGetUserId(context.User, out Guid userId))
            {
                return Results.Unauthorized();
            }

            Result<PagedResult<WorkflowTaskDto>> result = await handler.HandleAsync(
                new GetMyTasksQuery(
                    userId, !(includeCompleted ?? false), page ?? 1, pageSize ?? 25),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new AuthenticatedUserOnlyAttribute(
                "Reading one's own inbox needs no permission. Gating it would mean granting "
                + "that permission to everyone, which makes it meaningless."))
            .Produces<PagedResult<WorkflowTaskDto>>(StatusCodes.Status200OK)
            .WithName("GetMyTasks")
            .WithSummary("The caller's approval inbox.");

        tasks.MapPost("/{id:guid}/actions", async (
            Guid id,
            TaskActionRequest request,
            HttpContext context,
            [FromServices] ActOnTaskHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result validation = request.Validate();

            if (validation.IsFailure)
            {
                return validation.ToHttpResult(context, requestContext);
            }

            // The actor comes from the token, never the body. This is the rule
            // whose failure means somebody approved something that was never
            // theirs, and a caller who could name themselves would be exempt.
            if (!CallerIdentity.TryGetUserId(context.User, out Guid actorUserId))
            {
                return Results.Unauthorized();
            }

            Result<WorkflowInstanceDto> result = await handler.HandleAsync(
                new ActOnTaskCommand(
                    id, request.ParsedAction, actorUserId,
                    request.Comment, request.DelegateToUserId),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new AuthenticatedUserOnlyAttribute(
                "Acting on one's own task is authorised by being the assignee, which the "
                + "handler checks and the aggregate checks again. A permission would say "
                + "less, not more."))
            .Produces<WorkflowInstanceDto>(StatusCodes.Status200OK)
            .WithName("ActOnTask")
            .WithSummary("Approves, rejects, returns, delegates or comments on a task.");

        tasks.MapPost("/{id:guid}/reassign", async (
            Guid id,
            ReassignTaskRequest request,
            HttpContext context,
            [FromServices] ReassignTaskHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            // The actor comes from the token, like every other action here. It
            // is recorded as the person who took somebody else's approval away
            // from them, so it must not be something a caller can choose.
            if (!CallerIdentity.TryGetUserId(context.User, out Guid actorUserId))
            {
                return Results.Unauthorized();
            }

            Result result = await handler.HandleAsync(
                new ReassignTaskCommand(id, request.AssigneeUserId, actorUserId, request.Reason),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()

            // `manage`, not `start`. Moving somebody else's approval is an
            // administrative act on the engine, not the ordinary business of
            // raising a request -- and somebody who can start an approval should
            // not thereby be able to choose who approves it, which is what an
            // assignee rule exists to decide.
            .WithMetadata(new RequirePermissionAttribute("platform.workflow.manage"))
            .WithName("ReassignTask")
            .WithSummary("Moves a pending task to somebody else, over the assignee's head.");
    }
}

// ---------------------------------------------------------------------------
// Request bodies
// ---------------------------------------------------------------------------

/// <summary>One step, as an application registers it.</summary>
public sealed record StepRequest(
    string Key,
    string NameAr,
    string NameEn,
    int Order,
    string AssigneeStrategy,
    Guid? AssigneeUserId = null,
    Guid? AssigneeRoleId = null,
    Guid? AssigneePositionId = null,
    Guid? AssigneeUnitId = null,
    double? ServiceLevelHours = null,
    IReadOnlyList<TransitionRequest>? Transitions = null);

/// <summary>One allowed move. A null target ends the instance.</summary>
public sealed record TransitionRequest(string Action, string? TargetStepKey = null);

/// <summary>Register-definition request body.</summary>
public sealed record RegisterDefinitionRequest(
    string ApplicationCode,
    string Code,
    int Version,
    string NameAr,
    string NameEn,
    IReadOnlyList<StepRequest> Steps,
    string? Description = null,
    string? InitialStepKey = null)
{
    private List<StepSpecification> _steps = [];

    public Result Validate()
    {
        List<Error> errors = [];

        if (string.IsNullOrWhiteSpace(ApplicationCode))
        {
            errors.Add(Error.Validation(
                "WORKFLOW.APPLICATION_CODE_REQUIRED",
                "Say which application owns this process.",
                "applicationCode"));
        }

        if (string.IsNullOrWhiteSpace(Code))
        {
            errors.Add(Error.Validation(
                "WORKFLOW.DEFINITION_CODE_REQUIRED", "A process needs a code.", "code"));
        }

        if (Steps is null || Steps.Count == 0)
        {
            errors.Add(Error.Validation(
                "WORKFLOW.DEFINITION_HAS_NO_STEPS", "A process needs at least one step.", "steps"));

            return Result.Failure(errors);
        }

        var specifications = new List<StepSpecification>(Steps.Count);

        foreach (StepRequest step in Steps)
        {
            if (!Enum.TryParse(step.AssigneeStrategy, ignoreCase: true, out AssigneeStrategy strategy))
            {
                errors.Add(Error.Validation(
                    "WORKFLOW.UNKNOWN_ASSIGNEE_STRATEGY",
                    $"'{step.AssigneeStrategy}' is not a way of choosing an assignee.",
                    "assigneeStrategy"));

                continue;
            }

            var transitions = new List<TransitionSpecification>();

            foreach (TransitionRequest transition in step.Transitions ?? [])
            {
                if (!Enum.TryParse(transition.Action, ignoreCase: true, out WorkflowActionType action))
                {
                    errors.Add(Error.Validation(
                        "WORKFLOW.UNKNOWN_ACTION",
                        $"'{transition.Action}' is not an action the engine knows.",
                        "action"));

                    continue;
                }

                transitions.Add(new TransitionSpecification(action, transition.TargetStepKey));
            }

            specifications.Add(new StepSpecification(
                step.Key, step.NameAr, step.NameEn, step.Order, strategy,
                step.AssigneeUserId, step.AssigneeRoleId, step.AssigneePositionId,
                step.AssigneeUnitId, step.ServiceLevelHours, transitions));
        }

        if (errors.Count > 0)
        {
            return Result.Failure(errors);
        }

        _steps = specifications;

        return Result.Success();
    }

    /// <summary>Only meaningful after <see cref="Validate"/> succeeds.</summary>
    public RegisterDefinitionCommand ToCommand()
        => new(ApplicationCode, Code, Version, NameAr, NameEn,
            Description, InitialStepKey, _steps);
}

/// <summary>
/// Start-instance request body.
/// <para>
/// <c>Assignees</c> is where an application supplies routing it worked out
/// itself — the escape hatch that keeps business conditions outside the engine.
/// </para>
/// </summary>
public sealed record StartInstanceRequest(
    string ApplicationCode,
    string DefinitionCode,
    string ResourceType,
    string ResourceId,
    IReadOnlyList<Guid>? Assignees = null)
{
    public Result Validate()
        => string.IsNullOrWhiteSpace(ApplicationCode)
        || string.IsNullOrWhiteSpace(DefinitionCode)
        || string.IsNullOrWhiteSpace(ResourceType)
        || string.IsNullOrWhiteSpace(ResourceId)
            ? Result.Failure(Error.Validation(
                "WORKFLOW.START_INCOMPLETE",
                "Starting an approval needs the application, the process, and what is being approved.",
                "applicationCode"))
            : Result.Success();
}

/// <summary>Task-action request body.</summary>
public sealed record TaskActionRequest(
    string Action,
    string? Comment = null,
    Guid? DelegateToUserId = null)
{
    /// <summary>Only meaningful after <see cref="Validate"/> succeeds.</summary>
    public WorkflowActionType ParsedAction { get; private set; }

    public Result Validate()
    {
        if (!Enum.TryParse(Action, ignoreCase: true, out WorkflowActionType parsed))
        {
            return Result.Failure(Error.Validation(
                "WORKFLOW.UNKNOWN_ACTION",
                $"'{Action}' is not an action the engine knows.",
                "action"));
        }

        ParsedAction = parsed;

        if (parsed == WorkflowActionType.Delegate && DelegateToUserId is null)
        {
            return Result.Failure(Error.Validation(
                "WORKFLOW.INVALID_DELEGATE",
                "Delegating needs somebody to delegate to.",
                "delegateToUserId"));
        }

        return Result.Success();
    }
}

/// <summary>Cancel-instance request body.</summary>
public sealed record CancelInstanceRequest(string? Reason = null);

/// <summary>
/// Moving a task to somebody else.
/// </summary>
/// <param name="Reason">
/// Why. Required, because "they left the company" and "I wanted it approved
/// faster" are the same operation and very different acts, and six months later
/// this sentence is the only thing that tells them apart.
/// </param>
public sealed record ReassignTaskRequest(Guid AssigneeUserId, string Reason);
