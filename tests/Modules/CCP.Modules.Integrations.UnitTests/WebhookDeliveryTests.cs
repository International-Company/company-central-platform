using CCP.Modules.Integrations.Domain.Webhooks;

namespace CCP.Modules.Integrations.UnitTests;

/// <summary>
/// One event on its way to one subscriber.
/// <para>
/// <b>A row each, not a row with a list of recipients.</b> One subscriber being
/// down must not hold up another, and a single row cannot express "delivered to
/// two of three, retrying the third" — which is the ordinary state of affairs
/// rather than an edge case.
/// </para>
/// </summary>
public sealed class WebhookDeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AQueuedDeliveryIsDueImmediately()
    {
        WebhookDelivery delivery = Queue();

        Assert.Equal(WebhookDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(Now, delivery.NextAttemptAt);
        Assert.Equal(0, delivery.Attempts);
    }

    [Fact]
    public void ASuccessIsFinal()
    {
        WebhookDelivery delivery = Queue();

        delivery.RecordSuccess(200, Now);

        Assert.Equal(WebhookDeliveryStatus.Delivered, delivery.Status);
        Assert.Equal(200, delivery.ResponseStatusCode);
        Assert.Equal(Now, delivery.DeliveredAt);
        Assert.Null(delivery.LastError);
    }

    [Fact]
    public void AFailureSchedulesTheNextAttempt()
    {
        WebhookDelivery delivery = Queue();

        bool last = delivery.RecordFailure(
            500, "boom", maximumAttempts: 3, TimeSpan.FromMinutes(1), Now);

        Assert.False(last);
        Assert.Equal(WebhookDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(1, delivery.Attempts);
        Assert.Equal(Now.AddMinutes(1), delivery.NextAttemptAt);
        Assert.Equal(500, delivery.ResponseStatusCode);
    }

    /// <summary>
    /// Abandoned, and kept.
    /// <para>
    /// A row saying "we tried three times over an hour and your endpoint refused
    /// every one" is the answer to the question the subscriber will eventually
    /// ask. A delivery mechanism that erased its own failures could only answer
    /// with an opinion.
    /// </para>
    /// </summary>
    [Fact]
    public void GivingUpKeepsTheRecord()
    {
        WebhookDelivery delivery = Queue();

        delivery.RecordFailure(500, "boom", 3, TimeSpan.FromMinutes(1), Now);
        delivery.RecordFailure(500, "boom", 3, TimeSpan.FromMinutes(2), Now);

        Assert.True(delivery.RecordFailure(503, "still boom", 3, TimeSpan.FromMinutes(4), Now));

        Assert.Equal(WebhookDeliveryStatus.Abandoned, delivery.Status);
        Assert.Equal(3, delivery.Attempts);
        Assert.Equal(503, delivery.ResponseStatusCode);
        Assert.Equal("still boom", delivery.LastError);
    }

    /// <summary>
    /// The error is truncated, because the body of a failure response is written
    /// by somebody else's server and is quite often a megabyte of HTML.
    /// </summary>
    [Fact]
    public void AnEnormousErrorIsCutDown()
    {
        WebhookDelivery delivery = Queue();

        delivery.RecordFailure(500, new string('x', 5000), 3, TimeSpan.FromMinutes(1), Now);

        Assert.Equal(500, delivery.LastError!.Length);
    }

    private static WebhookDelivery Queue()
        => WebhookDelivery.Queue(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "a.happened", """{"a":1}""", Now);
}
