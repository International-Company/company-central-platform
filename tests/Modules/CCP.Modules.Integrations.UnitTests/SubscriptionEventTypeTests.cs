using CCP.Kernel.Application.Events;
using CCP.Kernel.Results;
using CCP.Modules.Integrations.Application.Webhooks;

namespace CCP.Modules.Integrations.UnitTests;

/// <summary>
/// A subscription may only name event types the Platform sends.
/// <para>
/// <b>Nothing checked before.</b> The portal's subscription form is a free-text
/// field, so a typo — <c>workflow.instance.complete</c> for
/// <c>workflow.instance.completed</c> — was accepted without complaint and the
/// subscription then received nothing, silently and for ever. Five declared
/// types could never be sent at all, and naming one of those was accepted too.
/// </para>
/// </summary>
public sealed class SubscriptionEventTypeTests
{
    private static readonly StubCatalogue Catalogue =
        new("workflow.instance.completed", "workflow.task.assigned");

    [Fact]
    public void KnownTypesAreAccepted()
    {
        Result result = RegisterSubscriptionHandler.CheckEventTypes(
            Catalogue, ["workflow.instance.completed", "workflow.task.assigned"]);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ATypoIsRefused()
    {
        Result result = RegisterSubscriptionHandler.CheckEventTypes(
            Catalogue, ["workflow.instance.complete"]);

        Assert.True(result.IsFailure);
        Assert.Equal("INTEGRATIONS.SUBSCRIPTION_EVENT_TYPES_UNKNOWN", result.Errors[0].Code);
    }

    /// <summary>
    /// The refusal names what was wrong and nothing that was right.
    /// <para>
    /// A subscription can name a dozen types. "One of them is wrong" makes
    /// somebody check all twelve against a list they have to go and find.
    /// </para>
    /// </summary>
    [Fact]
    public void TheRefusalNamesOnlyTheUnknownTypes()
    {
        Result result = RegisterSubscriptionHandler.CheckEventTypes(
            Catalogue, ["workflow.task.assigned", "workflow.instance.complete", "nope.nothing"]);

        string message = result.Errors[0].Message;

        Assert.Contains("workflow.instance.complete", message, StringComparison.Ordinal);
        Assert.Contains("nope.nothing", message, StringComparison.Ordinal);
        Assert.DoesNotContain("workflow.task.assigned", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Matching is exact, because delivery is exact.
    /// <para>
    /// The fan-out looks subscriptions up by the event's type as written. A
    /// check that forgave case would accept a subscription the fan-out would
    /// then never match.
    /// </para>
    /// </summary>
    [Fact]
    public void MatchingIsExact()
    {
        Result result = RegisterSubscriptionHandler.CheckEventTypes(
            Catalogue, ["Workflow.Task.Assigned"]);

        Assert.True(result.IsFailure);
    }

    private sealed class StubCatalogue(params string[] types) : IEventTypeCatalogue
    {
        public IReadOnlyList<string> EventTypes { get; } = types;

        public bool IsKnown(string eventType) => types.Contains(eventType, StringComparer.Ordinal);
    }
}
