using CCP.Kernel.Results;
using CCP.Modules.Integrations.Domain.Webhooks;

namespace CCP.Modules.Integrations.UnitTests;

/// <summary>
/// A standing instruction to send the company's events to an address somebody
/// chose.
/// <para>
/// <b>That sentence is why this waited for the phase that owns outbound
/// calls.</b> Somebody who can register a URL and have the Platform post to it
/// has a proxy into the network the Platform runs in — so the address goes
/// through the same allow-list and the same guarded socket as every other
/// outbound call, and the refusals below are only the ones that need no network
/// lookup.
/// </para>
/// </summary>
public sealed class WebhookSubscriptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Application = Guid.CreateVersion7();

    [Fact]
    public void ARegisteredSubscriptionIsLive()
    {
        WebhookSubscription subscription = Register().Value;

        Assert.True(subscription.IsLive);
        Assert.Equal(0, subscription.ConsecutiveFailures);
    }

    /// <summary>
    /// There is no way to ask for everything, and that is a decision rather than
    /// an omission: a subscription receiving every event would receive ones
    /// added years later, and the first its owner would know is a parser failing
    /// on a shape nobody told them about.
    /// </summary>
    [Fact]
    public void ASubscriptionToNothingIsRefused()
    {
        Assert.True(Register(events: []).IsFailure);
        Assert.True(Register(events: ["  "]).IsFailure);
    }

    [Fact]
    public void EventTypesAreDeduplicatedAndOrdered()
    {
        WebhookSubscription subscription =
            Register(events: ["b.happened", "a.happened", "b.happened"]).Value;

        Assert.Equal(["a.happened", "b.happened"], subscription.EventTypes);
    }

    [Fact]
    public void ItWantsOnlyWhatItNamed()
    {
        WebhookSubscription subscription = Register(events: ["a.happened"]).Value;

        Assert.True(subscription.Wants("a.happened"));
        Assert.False(subscription.Wants("b.happened"));

        // Ordinal. Event types are a contract between modules, and matching
        // "A.Happened" to "a.happened" would deliver on a name nobody declared.
        Assert.False(subscription.Wants("A.Happened"));
    }

    /// <summary>
    /// A subscription that cannot be signed is refused rather than sent
    /// unsigned. A webhook the receiver cannot authenticate is a message
    /// anybody on the internet can forge, and "we will add signing later" is how
    /// it never gets added.
    /// </summary>
    [Fact]
    public void ASubscriptionWithNoSigningSecretIsRefused()
    {
        Assert.True(Register(secret: "").IsFailure);
        Assert.True(Register(secret: "   ").IsFailure);
    }

    /// <summary>
    /// The refusals that need no network lookup. Whether the host is
    /// <i>allowed</i> is the outbound guard's answer and is the same answer for
    /// every outbound call.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("/relative/path")]
    [InlineData("ftp://files.example/drop")]
    [InlineData("file:///etc/passwd")]
    public void AnAddressThePlatformWillNotPostToIsRefused(string endpoint)
        => Assert.True(Register(endpoint: endpoint).IsFailure);

    /// <summary>
    /// Credentials in the URL, which is the trick that matters here:
    /// <c>https://trusted.example@evil.test/</c> points at evil.test and reads,
    /// to a human skimming a list of subscriptions, as the allowed host.
    /// </summary>
    [Fact]
    public void AnAddressCarryingCredentialsIsRefused()
        => Assert.True(Register(endpoint: "https://trusted.example@evil.test/hook").IsFailure);

    // --- Failure and suspension ---------------------------------------------

    [Fact]
    public void FailuresAccumulateUntilTheLimit()
    {
        WebhookSubscription subscription = Register().Value;

        Assert.False(subscription.RecordFailure(3, "gone", Now));
        Assert.False(subscription.RecordFailure(3, "gone", Now));
        Assert.True(subscription.RecordFailure(3, "gone", Now));

        Assert.False(subscription.IsLive);
        Assert.Equal("gone", subscription.SuspendedReason);
    }

    /// <summary>
    /// Consecutive, not cumulative. "This endpoint is gone" and "this endpoint
    /// had a bad week two years ago" are different facts, and only one of them
    /// is worth acting on.
    /// </summary>
    [Fact]
    public void ASuccessClearsTheCount()
    {
        WebhookSubscription subscription = Register().Value;

        subscription.RecordFailure(3, "gone", Now);
        subscription.RecordFailure(3, "gone", Now);
        subscription.RecordSuccess(Now);

        Assert.Equal(0, subscription.ConsecutiveFailures);
        Assert.False(subscription.RecordFailure(3, "gone", Now));
        Assert.True(subscription.IsLive);
    }

    /// <summary>
    /// Suspending is reported once.
    /// <para>
    /// A hundred queued events to a dead endpoint all fail in the same sweep,
    /// and an alert that fired a hundred times would be an alert somebody turns
    /// off.
    /// </para>
    /// </summary>
    [Fact]
    public void SuspendingIsAnnouncedOnce()
    {
        WebhookSubscription subscription = Register().Value;

        subscription.RecordFailure(2, "gone", Now);

        Assert.True(subscription.RecordFailure(2, "gone", Now));
        Assert.False(subscription.RecordFailure(2, "gone", Now));
        Assert.False(subscription.RecordFailure(2, "gone", Now));
    }

    /// <summary>
    /// Resuming clears the count that suspended it.
    /// <para>
    /// Otherwise the next single failure suspends it again, which looks to its
    /// owner exactly like resuming having done nothing.
    /// </para>
    /// </summary>
    [Fact]
    public void ResumingClearsWhatSuspendedIt()
    {
        WebhookSubscription subscription = Register().Value;

        subscription.RecordFailure(1, "gone", Now);

        subscription.Resume(Now.AddHours(1));

        Assert.True(subscription.IsLive);
        Assert.Equal(0, subscription.ConsecutiveFailures);
        Assert.Null(subscription.SuspendedReason);
    }

    /// <summary>
    /// A switched-off subscription is not live, and switching it off is a
    /// different state from being suspended: one is somebody's decision, the
    /// other is the Platform giving up.
    /// </summary>
    [Fact]
    public void SwitchingOffIsNotSuspending()
    {
        WebhookSubscription subscription = Register().Value;

        subscription.SetEnabled(false, Now);

        Assert.False(subscription.IsLive);
        Assert.Null(subscription.SuspendedAt);
    }

    // --- Fixtures -----------------------------------------------------------

    private static Result<WebhookSubscription> Register(
        string endpoint = "https://acme.test/hooks",
        string[]? events = null,
        string secret = "integrations/acme/webhook-secret")
        => WebhookSubscription.Register(
            Application, "Acme", endpoint, events ?? ["a.happened"], secret, Now);
}
