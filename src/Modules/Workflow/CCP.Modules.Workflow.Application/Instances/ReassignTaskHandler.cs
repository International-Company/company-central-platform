using CCP.Kernel.Application.Auditing;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Workflow.Application.Abstractions;
using CCP.Modules.Workflow.Application.Engine;
using CCP.Modules.Workflow.Domain;
using CCP.Modules.Workflow.Domain.Instances;

namespace CCP.Modules.Workflow.Application.Instances;

/// <summary>Moving a task to somebody else over the assignee's head.</summary>
/// <param name="Reason">
/// Why. Required, and it is the only part of this that a person reads six months
/// later — "they left the company" and "I wanted it approved faster" are the
/// same operation and very different acts.
/// </param>
public sealed record ReassignTaskCommand(
    Guid TaskId,
    Guid NewAssigneeUserId,
    Guid ActorUserId,
    string Reason);

/// <summary>
/// The way out of a task nobody can act on.
/// <para>
/// <b>An assignee who has left the company is a dead end, and it was a complete
/// one.</b> Delegation needs the assignee to act, and escalation deliberately
/// raises an event without reassigning — both of those are right, and together
/// they meant a task held by a disabled account sat pending for ever with the
/// approval behind it unable to finish. There was no recovery path at all.
/// </para>
/// <para>
/// <b>Gated on <c>platform.workflow.manage</c>, not on <c>start</c>.</b> Moving
/// somebody else's approval is an administrative act on the engine, not the
/// ordinary business of raising a request. Somebody who can start an approval
/// should not thereby be able to choose who approves it — which is the whole
/// point of an assignee rule.
/// </para>
/// </summary>
public sealed class ReassignTaskHandler(
    IWorkflowRepository repository,
    IWorkflowUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        ReassignTaskCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure(WorkflowErrors.ReasonRequired);
        }

        WorkflowTask? task = await repository.FindTaskAsync(command.TaskId, cancellationToken);

        if (task is null)
        {
            return Result.Failure(WorkflowErrors.TaskNotFound);
        }

        Guid previousAssignee = task.AssignedToUserId;

        Result reassigned = task.ReassignTo(command.NewAssigneeUserId, clock.UtcNow);

        if (reassigned.IsFailure)
        {
            return reassigned;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Both people and the reason. This is the one action in the engine where
        // somebody's name is taken off an approval by a third party, and a trail
        // that recorded only the new assignee would lose the fact that anything
        // was taken away from anyone.
        await auditTrail.RecordAsync(
            WorkflowAudit.Action(
                "task.reassigned",
                task.Id,
                $$"""
                {"from":"{{previousAssignee}}","to":"{{task.AssignedToUserId}}","by":"{{command.ActorUserId}}","reason":{{Quoted(command.Reason)}}}
                """),
            cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// The reason as a JSON string, with quotes and backslashes escaped.
    /// <para>
    /// It is free text a person typed. Interpolating it raw would let a quote
    /// mark produce a malformed audit record — which is worse than an ugly one,
    /// because the trail is what a dispute is settled from.
    /// </para>
    /// </summary>
    private static string Quoted(string value) =>
        $"\"{value.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
