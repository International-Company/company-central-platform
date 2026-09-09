using CCP.Kernel.Results;
using CCP.Modules.Workflow.Domain;
using CCP.Modules.Workflow.Domain.Definitions;

namespace CCP.Modules.Workflow.UnitTests;

/// <summary>
/// What a definition refuses to become.
/// <para>
/// Publication is the only moment a process is checked, so it is the only moment
/// a broken one can be caught. Everything asserted here would otherwise be
/// discovered by a person waiting on a step that leads nowhere.
/// </para>
/// </summary>
public sealed class WorkflowDefinitionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void APublishedDefinitionCannotGainAStep()
    {
        WorkflowDefinition definition = Published(
            Step("only", ("Approve", null)));

        Result added = definition.AddStep(Step("late"), Now);

        // Instances are running on it. A step appearing mid-flight is a
        // transition nobody validated, in a process somebody is halfway through.
        Assert.True(added.IsFailure);
        Assert.Equal("WORKFLOW.DEFINITION_NOT_DRAFT", added.Errors[0].Code);
    }

    [Fact]
    public void ATransitionToANonexistentStepIsRefusedAtPublication()
    {
        WorkflowDefinition definition = Draft();

        definition.AddStep(Step("start", ("Approve", "nowhere")), Now);

        Result published = definition.Publish(Now);

        Assert.True(published.IsFailure);
        Assert.Equal("WORKFLOW.TRANSITION_TARGET_NOT_FOUND", published.Errors[0].Code);
    }

    [Fact]
    public void AnUnreachableStepIsRefusedAtPublication()
    {
        WorkflowDefinition definition = Draft();

        definition.AddStep(Step("start", ("Approve", null)), Now);
        definition.AddStep(Step("orphan", ("Approve", null)), Now);

        Result published = definition.Publish(Now);

        // Either a mistake or a leftover, and both are worth refusing before
        // anybody relies on the process.
        Assert.True(published.IsFailure);
        Assert.Equal("WORKFLOW.STEP_UNREACHABLE", published.Errors[0].Code);
    }

    [Fact]
    public void ACycleIsAllowed()
    {
        WorkflowDefinition definition = Draft();

        definition.AddStep(Step("review", ("Approve", "approval")), Now);
        definition.AddStep(Step("approval", ("Return", "review"), ("Approve", null)), Now);

        // A return sends work back, which is a cycle by construction. The
        // reachability check walks the graph and must not mistake one for an
        // infinite loop — the first version of that walk would have.
        Assert.True(definition.Publish(Now).IsSuccess);
    }

    [Fact]
    public void ADefinitionWithNoStepsCannotBePublished()
    {
        Result published = Draft().Publish(Now);

        Assert.True(published.IsFailure);
        Assert.Equal("WORKFLOW.DEFINITION_HAS_NO_STEPS", published.Errors[0].Code);
    }

    [Fact]
    public void TheFirstStepAddedBecomesTheEntryPoint()
    {
        WorkflowDefinition definition = Draft();

        definition.AddStep(Step("first", ("Approve", null)), Now);

        // A convenience with a real consequence: a single-step process needs no
        // ceremony, and a multi-step one that forgets to name its entry point
        // starts where its author wrote first, which is almost always right.
        Assert.Equal("first", definition.InitialStepKey);
    }

    [Fact]
    public void TwoStepsCannotShareAKey()
    {
        WorkflowDefinition definition = Draft();

        definition.AddStep(Step("same"), Now);

        Result second = definition.AddStep(Step("same"), Now);

        Assert.True(second.IsFailure);
        Assert.Equal("WORKFLOW.DUPLICATE_STEP_KEY", second.Errors[0].Code);
    }

    [Fact]
    public void AStepCannotSayWhereOneActionLeadsTwice()
    {
        WorkflowStep step = Step("branching");

        step.AddTransition(WorkflowTransition.Create(step.Id, WorkflowActionType.Approve, "a", Now));

        Result duplicate = step.AddTransition(
            WorkflowTransition.Create(step.Id, WorkflowActionType.Approve, "b", Now));

        // Two "approve" transitions from one step is a process whose next state
        // depends on something the definition does not say.
        Assert.True(duplicate.IsFailure);
        Assert.Equal("WORKFLOW.DUPLICATE_TRANSITION", duplicate.Errors[0].Code);
    }

    [Fact]
    public void RetiringLeavesTheDefinitionReadableForRunningInstances()
    {
        WorkflowDefinition definition = Published(Step("only", ("Approve", null)));

        Assert.True(definition.Retire(Now).IsSuccess);
        Assert.Equal(DefinitionStatus.Retired, definition.Status);

        // Retiring is a statement about the future. The steps are still there,
        // because instances started before it still need to be evaluated.
        Assert.NotNull(definition.FindStep("only"));
    }

    [Fact]
    public void AServiceLevelOfZeroIsRefused()
    {
        Result<WorkflowStep> step = WorkflowStep.Create(
            Guid.CreateVersion7(), "s", "خطوة", "Step", 1,
            AssigneeRule.ForRequesterManager(), TimeSpan.Zero, Now);

        // A deadline of "now" fires on the first sweep, for every task, forever.
        Assert.True(step.IsFailure);
        Assert.Equal("WORKFLOW.SERVICE_LEVEL_INVALID", step.Errors[0].Code);
    }

    // -----------------------------------------------------------------------

    private static WorkflowDefinition Draft()
        => WorkflowDefinition.Create("testing", "process", 1, "عملية", "Process", null, Now).Value;

    private static WorkflowDefinition Published(params WorkflowStep[] steps)
    {
        WorkflowDefinition definition = Draft();

        foreach (WorkflowStep step in steps)
        {
            definition.AddStep(step, Now);
        }

        Assert.True(definition.Publish(Now).IsSuccess);

        return definition;
    }

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
