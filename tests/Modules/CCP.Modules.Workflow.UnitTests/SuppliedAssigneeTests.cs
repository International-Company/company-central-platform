using CCP.Kernel.Results;
using CCP.Modules.Workflow.Domain;
using CCP.Modules.Workflow.Domain.Definitions;

namespace CCP.Modules.Workflow.UnitTests;

/// <summary>
/// The one escape from "the engine holds no business rules".
/// <para>
/// ARCHITECTURE.md §16.3 says the workflow engine routes by a person's place in
/// the company and never by what is being approved — no "if amount &gt; X then Y".
/// That is the boundary which keeps the module reusable, and it is only
/// defensible because there is a way out: the calling application, which does
/// understand the amount, works out who should act and names them when it starts
/// the request.
/// </para>
/// <para>
/// <b>That escape had no test.</b> Debt #28 leans on it, the module's own
/// documentation leans on it, and the answer to every "how do I route on
/// business data" question is it. Nothing exercised it, so nothing would have
/// noticed it breaking — and the question would simply have had no answer.
/// </para>
/// <para>
/// Writing these found that it half worked, which is the part below about a
/// later step.
/// </para>
/// </summary>
public sealed class SuppliedAssigneeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The first step may ask the caller who should act.
    /// <para>
    /// This is the escape working: the definition publishes, and the engine will
    /// take its assignees from the start request.
    /// </para>
    /// </summary>
    [Fact]
    public void TheFirstStepMayAskTheCaller()
    {
        WorkflowDefinition definition = Draft();

        definition.AddStep(SuppliedStep("triage", ("Approve", null)), Now);

        Assert.True(definition.Publish(Now).IsSuccess);
    }

    /// <summary>
    /// A later step may not, and publication is where it is told so.
    /// <para>
    /// <b>This was accepted before.</b> The definition published happily, and
    /// the engine enters every step after the first with an empty supplied list
    /// — deliberately, because a step reached later is reached by somebody
    /// acting rather than by the caller, and carrying a start-time list forward
    /// would quietly turn it into engine state.
    /// </para>
    /// <para>
    /// So the step resolved to nobody, the transition into it failed, and the
    /// person who filed the request three weeks earlier was told that "nobody
    /// could be found for this step — check that the role, position or manager
    /// it names still exists". It names no role, position or manager. The
    /// definition could not have worked on the day it was written, and this is
    /// the day it is refused.
    /// </para>
    /// </summary>
    [Fact]
    public void ALaterStepMayNot()
    {
        WorkflowDefinition definition = Draft();

        definition.AddStep(Step("review", ("Approve", "decide")), Now);
        definition.AddStep(SuppliedStep("decide", ("Approve", null)), Now);

        Result published = definition.Publish(Now);

        Assert.True(published.IsFailure);
        Assert.Equal("WORKFLOW.SUPPLIED_BY_CALLER_ON_LATER_STEP", published.Errors[0].Code);
    }

    /// <summary>
    /// The refusal names the step.
    /// <para>
    /// A definition can have a dozen steps. "One of them is wrong" is a message
    /// that makes somebody read all twelve.
    /// </para>
    /// </summary>
    [Fact]
    public void TheRefusalNamesTheStep()
    {
        WorkflowDefinition definition = Draft();

        definition.AddStep(Step("review", ("Approve", "decide")), Now);
        definition.AddStep(SuppliedStep("decide", ("Approve", null)), Now);

        Result published = definition.Publish(Now);

        Assert.Contains("decide", published.Errors[0].Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The ordinary strategies are unaffected on a later step.
    /// <para>
    /// The check must refuse one strategy in one position, not steps after the
    /// first. A guard that over-reaches is removed by whoever it blocks next.
    /// </para>
    /// </summary>
    [Fact]
    public void AnOrdinaryStrategyIsStillFineLaterOn()
    {
        WorkflowDefinition definition = Draft();

        definition.AddStep(Step("review", ("Approve", "decide")), Now);
        definition.AddStep(Step("decide", ("Approve", null)), Now);

        Assert.True(definition.Publish(Now).IsSuccess);
    }

    // -----------------------------------------------------------------------

    private static WorkflowDefinition Draft()
        => WorkflowDefinition.Create("testing", "process", 1, "عملية", "Process", null, Now).Value;

    private static WorkflowStep Step(string key, params (string Action, string? Target)[] transitions)
        => Build(key, AssigneeRule.ForRequesterManager(), transitions);

    private static WorkflowStep SuppliedStep(
        string key, params (string Action, string? Target)[] transitions)
        => Build(key, AssigneeRule.ForSuppliedList(), transitions);

    private static WorkflowStep Build(
        string key, AssigneeRule rule, (string Action, string? Target)[] transitions)
    {
        WorkflowStep step = WorkflowStep.Create(
            Guid.CreateVersion7(), key, key, key, 1, rule, null, Now).Value;

        foreach ((string action, string? target) in transitions)
        {
            step.AddTransition(WorkflowTransition.Create(
                step.Id, Enum.Parse<WorkflowActionType>(action), target, Now));
        }

        return step;
    }
}
