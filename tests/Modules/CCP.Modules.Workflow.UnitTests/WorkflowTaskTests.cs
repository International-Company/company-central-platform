using CCP.Kernel.Results;
using CCP.Modules.Workflow.Domain.Definitions;
using CCP.Modules.Workflow.Domain.Instances;

namespace CCP.Modules.Workflow.UnitTests;

/// <summary>
/// Who may act on a task, and what becomes of it.
/// <para>
/// The assignee rule is the one whose failure means somebody approved something
/// that was never theirs to approve. It is checked in the handler and again
/// here, and this suite is why the second check can be trusted.
/// </para>
/// </summary>
public sealed class WorkflowTaskTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Assignee = Guid.CreateVersion7();
    private static readonly Guid Stranger = Guid.CreateVersion7();

    [Fact]
    public void OnlyTheAssigneeMayComplete()
    {
        WorkflowTask task = Pending();

        Result completed = task.Complete(WorkflowActionType.Approve, Stranger, Now);

        Assert.True(completed.IsFailure);
        Assert.Equal("WORKFLOW.NOT_THE_ASSIGNEE", completed.Errors[0].Code);
        Assert.Equal(WorkflowTaskStatus.Pending, task.Status);
    }

    [Fact]
    public void CompletingRecordsWhichActionSettledIt()
    {
        WorkflowTask task = Pending();

        Assert.True(task.Complete(WorkflowActionType.Reject, Assignee, Now).IsSuccess);

        // "Completed" alone would not say whether the person agreed. A report of
        // who approved what needs the verb.
        Assert.Equal(WorkflowTaskStatus.Completed, task.Status);
        Assert.Equal(WorkflowActionType.Reject, task.CompletedWith);
        Assert.Equal(Now, task.CompletedAt);
    }

    [Fact]
    public void ASettledTaskCannotBeSettledAgain()
    {
        WorkflowTask task = Pending();

        task.Complete(WorkflowActionType.Approve, Assignee, Now);

        Result again = task.Complete(WorkflowActionType.Reject, Assignee, Now);

        Assert.True(again.IsFailure);
        Assert.Equal("WORKFLOW.TASK_NOT_PENDING", again.Errors[0].Code);
        Assert.Equal(WorkflowActionType.Approve, task.CompletedWith);
    }

    [Fact]
    public void DelegationMovesTheTaskAndRemembersWhereItCameFrom()
    {
        WorkflowTask task = Pending();

        Assert.True(task.DelegateTo(Assignee, Stranger, Now).IsSuccess);

        Assert.Equal(Stranger, task.AssignedToUserId);
        Assert.Equal(Assignee, task.DelegatedFromUserId);

        // Still waiting: delegation changes who is being waited for, not what.
        Assert.Equal(WorkflowTaskStatus.Pending, task.Status);

        // And the new holder may now act, while the old one may not.
        Assert.True(task.MayBeActedOnBy(Stranger));
        Assert.False(task.MayBeActedOnBy(Assignee));
    }

    [Fact]
    public void ATaskCannotBeDelegatedToItsOwnAssignee()
    {
        WorkflowTask task = Pending();

        Result self = task.DelegateTo(Assignee, Assignee, Now);

        Assert.True(self.IsFailure);
        Assert.Equal("WORKFLOW.INVALID_DELEGATE", self.Errors[0].Code);
    }

    [Fact]
    public void OnlyTheAssigneeMayDelegate()
    {
        WorkflowTask task = Pending();

        Result stolen = task.DelegateTo(Stranger, Stranger, Now);

        Assert.True(stolen.IsFailure);
        Assert.Equal("WORKFLOW.NOT_THE_ASSIGNEE", stolen.Errors[0].Code);
    }

    [Fact]
    public void WithdrawingIsNotCompleting()
    {
        WorkflowTask task = Pending();

        Assert.True(task.Withdraw(Now).IsSuccess);

        // A colleague acted first. Nobody acted on this one, and a report of who
        // approved what must not count it.
        Assert.Equal(WorkflowTaskStatus.Withdrawn, task.Status);
        Assert.Null(task.CompletedWith);
    }

    [Fact]
    public void ATaskWithNoDeadlineIsNeverOverdue()
    {
        WorkflowTask task = Pending();

        Assert.Null(task.DueAt);
        Assert.False(task.IsOverdue(Now.AddYears(10)));
    }

    [Fact]
    public void ADeadlineIsCountedFromAssignment()
    {
        WorkflowTask task = Pending(TimeSpan.FromHours(48));

        Assert.Equal(Now.AddHours(48), task.DueAt);
        Assert.False(task.IsOverdue(Now.AddHours(47)));
        Assert.True(task.IsOverdue(Now.AddHours(49)));
    }

    [Fact]
    public void EscalationHappensOnce()
    {
        WorkflowTask task = Pending(TimeSpan.FromHours(1));

        Assert.True(task.Escalate(Now.AddHours(2)).IsSuccess);

        Result again = task.Escalate(Now.AddHours(3));

        // A timer that fires on every sweep sends a reminder every five minutes
        // until somebody acts, and people learn to filter it — at which point
        // the escalation has made the problem harder to see.
        Assert.True(again.IsFailure);
        Assert.Equal("WORKFLOW.TASK_ALREADY_ESCALATED", again.Errors[0].Code);
    }

    [Fact]
    public void AnEscalatedTaskIsStillWaiting()
    {
        WorkflowTask task = Pending(TimeSpan.FromHours(1));

        task.Escalate(Now.AddHours(2));

        // Escalation tells somebody. It does not decide, and it does not move
        // the work: that would be the Platform making an organizational choice
        // on the company's behalf.
        Assert.Equal(WorkflowTaskStatus.Pending, task.Status);
        Assert.Equal(Assignee, task.AssignedToUserId);
        Assert.True(task.MayBeActedOnBy(Assignee));
    }

    [Fact]
    public void ATaskNeedsSomebodyToDoIt()
    {
        Result<WorkflowTask> nobody = WorkflowTask.Assign(
            Guid.CreateVersion7(), "step", Guid.Empty, null, Now);

        Assert.True(nobody.IsFailure);
        Assert.Equal("WORKFLOW.ASSIGNEE_REQUIRED", nobody.Errors[0].Code);
    }

    private static WorkflowTask Pending(TimeSpan? serviceLevel = null)
        => WorkflowTask.Assign(
            Guid.CreateVersion7(), "step", Assignee, serviceLevel, Now).Value;
}
