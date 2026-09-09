using CCP.Kernel.Results;
using CCP.Modules.Workflow.Domain.Definitions;
using CCP.Modules.Workflow.Domain.Instances;

namespace CCP.Modules.Workflow.UnitTests;

/// <summary>
/// The state machine, one transition at a time.
/// <para>
/// Every assertion here is a thing that would otherwise be discovered by
/// somebody's approval going to the wrong place, or by a decision recorded that
/// nobody made.
/// </para>
/// </summary>
public sealed class WorkflowInstanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Requester = Guid.CreateVersion7();
    private static readonly Guid Actor = Guid.CreateVersion7();

    [Fact]
    public void AnInstanceCannotStartOnADraft()
    {
        WorkflowDefinition draft = Draft();

        draft.AddStep(Step("only", ("Approve", null)), Now);

        Result<WorkflowInstance> started = WorkflowInstance.Start(
            draft, "thing", "1", Requester, Now);

        // A draft has not been validated. The refusal is about the process, not
        // about the request.
        Assert.True(started.IsFailure);
        Assert.Equal("WORKFLOW.DEFINITION_NOT_PUBLISHED", started.Errors[0].Code);
    }

    [Fact]
    public void AnApprovalWithNoTargetCompletesTheInstance()
    {
        (WorkflowInstance instance, WorkflowDefinition definition) =
            Running(Step("only", ("Approve", null)));

        Result<WorkflowInstanceAction> acted = instance.Act(
            definition.FindStep("only")!, WorkflowActionType.Approve, Actor, "yes", Now);

        Assert.True(acted.IsSuccess);
        Assert.Equal(InstanceStatus.Approved, instance.Status);
        Assert.Null(instance.CurrentStepKey);
        Assert.Equal(Now, instance.CompletedAt);
    }

    [Fact]
    public void ARejectionCompletesItRejected()
    {
        (WorkflowInstance instance, WorkflowDefinition definition) =
            Running(Step("only", ("Reject", null)));

        instance.Act(definition.FindStep("only")!, WorkflowActionType.Reject, Actor, null, Now);

        // Approved and rejected are different facts, and a business system does
        // different things with them.
        Assert.Equal(InstanceStatus.Rejected, instance.Status);
    }

    [Fact]
    public void AReturnWithNoTargetIsRefusedAndRecordsNothing()
    {
        (WorkflowInstance instance, WorkflowDefinition definition) =
            Running(Step("only", ("Return", null)));

        Result<WorkflowInstanceAction> acted = instance.Act(
            definition.FindStep("only")!, WorkflowActionType.Return, Actor, null, Now);

        // The defect an architecture test found before this code ever ran: the
        // first version fell through to Approved, recording an approval nobody
        // made on a request somebody had just sent back.
        Assert.True(acted.IsFailure);
        Assert.Equal("WORKFLOW.RETURN_NEEDS_A_TARGET", acted.Errors[0].Code);

        // And nothing is left behind. A half-applied action in the history is a
        // history that cannot be trusted.
        Assert.Equal(InstanceStatus.Running, instance.Status);
        Assert.Empty(instance.Actions);
    }

    [Fact]
    public void AnActionTheStepDoesNotPermitIsRefused()
    {
        (WorkflowInstance instance, WorkflowDefinition definition) =
            Running(Step("only", ("Approve", null)));

        Result<WorkflowInstanceAction> acted = instance.Act(
            definition.FindStep("only")!, WorkflowActionType.Reject, Actor, null, Now);

        Assert.True(acted.IsFailure);
        Assert.Equal("WORKFLOW.ACTION_NOT_PERMITTED", acted.Errors[0].Code);
        Assert.Equal(InstanceStatus.Running, instance.Status);
    }

    [Fact]
    public void ActingOnAStepTheInstanceHasLeftIsRefused()
    {
        (WorkflowInstance instance, WorkflowDefinition definition) = Running(
            Step("first", ("Approve", "second")),
            Step("second", ("Approve", null)));

        instance.Act(definition.FindStep("first")!, WorkflowActionType.Approve, Actor, null, Now);

        Result<WorkflowInstanceAction> late = instance.Act(
            definition.FindStep("first")!, WorkflowActionType.Approve, Actor, null, Now);

        // Two people looking at the same request, one of them with a stale
        // screen. The second must be told what happened, not silently applied.
        Assert.True(late.IsFailure);
        Assert.Equal("WORKFLOW.STEP_NOT_CURRENT", late.Errors[0].Code);
    }

    [Fact]
    public void ACommentChangesNothing()
    {
        (WorkflowInstance instance, WorkflowDefinition definition) =
            Running(Step("only", ("Approve", null)));

        Result<WorkflowInstanceAction> noted = instance.Act(
            definition.FindStep("only")!, WorkflowActionType.Comment, Actor, "a thought", Now);

        // Saying something about a decision is not taking it — and a comment is
        // allowed even where the step permits no other action.
        Assert.True(noted.IsSuccess);
        Assert.Equal(InstanceStatus.Running, instance.Status);
        Assert.Equal("only", instance.CurrentStepKey);
        Assert.Single(instance.Actions);
    }

    [Fact]
    public void AFinishedInstanceAcceptsNothingFurther()
    {
        (WorkflowInstance instance, WorkflowDefinition definition) =
            Running(Step("only", ("Approve", null)));

        instance.Act(definition.FindStep("only")!, WorkflowActionType.Approve, Actor, null, Now);

        Result<WorkflowInstanceAction> after = instance.Act(
            definition.FindStep("only")!, WorkflowActionType.Comment, Actor, "late", Now);

        Assert.True(after.IsFailure);
        Assert.Equal("WORKFLOW.INSTANCE_NOT_RUNNING", after.Errors[0].Code);
    }

    [Fact]
    public void CancellingWorksWhereTheDefinitionNeverAllowedIt()
    {
        (WorkflowInstance instance, _) = Running(Step("only", ("Approve", null)));

        // A requester withdrawing their own request must not depend on whoever
        // wrote the process having thought to allow it at every step.
        Assert.True(instance.Cancel(Requester, "changed my mind", Now).IsSuccess);
        Assert.Equal(InstanceStatus.Cancelled, instance.Status);
        Assert.Single(instance.Actions);
    }

    [Fact]
    public void TheHistoryKeepsTheActorAndTheComment()
    {
        (WorkflowInstance instance, WorkflowDefinition definition) =
            Running(Step("only", ("Approve", null)));

        instance.Act(definition.FindStep("only")!, WorkflowActionType.Approve, Actor, "  spaced  ", Now);

        WorkflowInstanceAction recorded = Assert.Single(instance.Actions);

        Assert.Equal(Actor, recorded.ActorUserId);
        Assert.Equal("only", recorded.StepKey);
        Assert.Equal("spaced", recorded.Comment);
        Assert.Equal(Now, recorded.OccurredAt);
    }

    [Fact]
    public void AnEmptyCommentIsStoredAsAbsent()
    {
        (WorkflowInstance instance, WorkflowDefinition definition) =
            Running(Step("only", ("Approve", null)));

        instance.Act(definition.FindStep("only")!, WorkflowActionType.Approve, Actor, "   ", Now);

        // Absence is a state; a string of spaces is a value nobody meant.
        Assert.Null(Assert.Single(instance.Actions).Comment);
    }

    // -----------------------------------------------------------------------

    private static (WorkflowInstance, WorkflowDefinition) Running(params WorkflowStep[] steps)
    {
        WorkflowDefinition definition = Draft();

        foreach (WorkflowStep step in steps)
        {
            definition.AddStep(step, Now);
        }

        Assert.True(definition.Publish(Now).IsSuccess);

        Result<WorkflowInstance> instance = WorkflowInstance.Start(
            definition, "thing", "1", Requester, Now);

        Assert.True(instance.IsSuccess);

        return (instance.Value, definition);
    }

    private static WorkflowDefinition Draft()
        => WorkflowDefinition.Create("testing", "process", 1, "عملية", "Process", null, Now).Value;

    private static WorkflowStep Step(string key, params (string Action, string? Target)[] transitions)
    {
        WorkflowStep step = WorkflowStep.Create(
            Guid.CreateVersion7(), key, key, key, 1,
            AssigneeRule.ForRequesterManager(), null, Now).Value;

        foreach ((string action, string? target) in transitions)
        {
            step.AddTransition(WorkflowTransition.Create(
                step.Id, Enum.Parse<WorkflowActionType>(action), target, Now));
        }

        return step;
    }
}
