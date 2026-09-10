using CCP.Modules.Workflow.Domain.Definitions;
using CCP.Modules.Workflow.Domain.Instances;

namespace CCP.Modules.Workflow.UnitTests.Domain;

/// <summary>
/// The way out of a task nobody can act on.
/// <para>
/// <b>An assignee who has left the company was a complete dead end.</b>
/// Delegation requires the assignee to act, and escalation deliberately raises
/// an event without reassigning. Both of those are right; together they meant a
/// task held by a disabled account sat pending for ever, with the approval
/// behind it unable to finish and nothing anywhere able to move it.
/// </para>
/// </summary>
public sealed class TaskReassignmentTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Assignee = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Replacement = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static WorkflowTask ATask() =>
        WorkflowTask.Assign(
            Guid.CreateVersion7(), "approve", Assignee, null, Now).Value;

    [Fact]
    public void APendingTaskMovesToTheNewAssignee()
    {
        WorkflowTask task = ATask();

        Assert.True(task.ReassignTo(Replacement, Now).IsSuccess);
        Assert.Equal(Replacement, task.AssignedToUserId);
    }

    /// <summary>
    /// <b>The distinction this class exists for.</b> Reassignment is not
    /// delegation, and must not claim to be: <c>DelegatedFromUserId</c> means
    /// "this person chose to pass it on", and setting it here would put words
    /// in the mouth of somebody who may have had no idea — who may, in the case
    /// this feature is for, have left the company.
    /// </summary>
    [Fact]
    public void ReassignmentDoesNotClaimTheAssigneeDelegated()
    {
        WorkflowTask task = ATask();

        task.ReassignTo(Replacement, Now);

        Assert.Null(task.DelegatedFromUserId);
    }

    /// <summary>
    /// A settled task is history. Moving one would rewrite who was asked to
    /// approve something that has already been approved.
    /// </summary>
    [Fact]
    public void ASettledTaskCannotBeMoved()
    {
        WorkflowTask task = ATask();

        task.Complete(WorkflowActionType.Approve, Assignee, Now);

        Assert.True(task.ReassignTo(Replacement, Now).IsFailure);
        Assert.Equal(Assignee, task.AssignedToUserId);
    }

    [Fact]
    public void AWithdrawnTaskCannotBeMoved()
    {
        WorkflowTask task = ATask();

        task.Withdraw(Now);

        Assert.True(task.ReassignTo(Replacement, Now).IsFailure);
    }

    /// <summary>
    /// Reassigning to the person who already holds it is refused rather than
    /// quietly accepted. It is almost always a mistaken click, and letting it
    /// through would write an audit record saying an approval was taken from
    /// somebody and given to them.
    /// </summary>
    [Fact]
    public void MovingATaskToItsCurrentHolderIsRefused()
    {
        WorkflowTask task = ATask();

        Assert.True(task.ReassignTo(Assignee, Now).IsFailure);
    }

    [Fact]
    public void MovingATaskToNobodyIsRefused()
    {
        WorkflowTask task = ATask();

        Assert.True(task.ReassignTo(Guid.Empty, Now).IsFailure);
        Assert.Equal(Assignee, task.AssignedToUserId);
    }

    /// <summary>
    /// An escalated task is exactly the case this feature is for — the service
    /// level has already been missed and the assignee still has not acted — so
    /// it must remain movable.
    /// </summary>
    [Fact]
    public void AnEscalatedTaskCanStillBeMoved()
    {
        WorkflowTask task = ATask();

        task.Escalate(Now);

        Assert.True(task.ReassignTo(Replacement, Now).IsSuccess);
        Assert.Equal(Replacement, task.AssignedToUserId);
    }

    /// <summary>
    /// And the new assignee can then act, which is the entire point: a
    /// reassignment that left the task unactionable would have moved the dead
    /// end rather than removed it.
    /// </summary>
    [Fact]
    public void TheNewAssigneeCanActOnIt()
    {
        WorkflowTask task = ATask();

        task.ReassignTo(Replacement, Now);

        Assert.True(task.Complete(WorkflowActionType.Approve, Replacement, Now).IsSuccess);
    }

    /// <summary>
    /// And the previous one cannot. Otherwise reassignment would add somebody
    /// rather than replace them.
    /// </summary>
    [Fact]
    public void ThePreviousAssigneeCanNoLongerActOnIt()
    {
        WorkflowTask task = ATask();

        task.ReassignTo(Replacement, Now);

        Assert.True(task.Complete(WorkflowActionType.Approve, Assignee, Now).IsFailure);
    }
}
