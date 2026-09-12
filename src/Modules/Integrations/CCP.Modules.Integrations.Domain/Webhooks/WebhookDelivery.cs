using CCP.Kernel.Domain;

namespace CCP.Modules.Integrations.Domain.Webhooks;

/// <summary>What became of one attempt to tell one subscriber about one event.</summary>
public enum WebhookDeliveryStatus
{
    /// <summary>Waiting, or waiting again after a failure.</summary>
    Pending = 1,

    /// <summary>The subscriber answered with a success status.</summary>
    Delivered = 2,

    /// <summary>
    /// Tried the agreed number of times and stopped.
    /// <para>
    /// Kept, not removed. A row that says "we tried six times over an hour and
    /// your endpoint refused every one" is the answer to the question the
    /// subscriber will eventually ask, and a delivery mechanism that erases its
    /// own failures cannot answer it.
    /// </para>
    /// </summary>
    Abandoned = 3
}

/// <summary>
/// One event, on its way to one subscriber.
/// <para>
/// <b>A row per subscriber, not a row per event.</b> One subscriber being down
/// must not hold up another's delivery, and a single row with a list of
/// recipients cannot express "delivered to two of three, retrying the third" —
/// which is the ordinary state of affairs, not an edge case.
/// </para>
/// <para>
/// The payload is stored rather than re-derived. The event that caused it may be
/// long gone from the outbox by the time a subscriber comes back, and a retry
/// that sent a freshly-computed body would send something other than what the
/// earlier attempts claimed to send.
/// </para>
/// </summary>
public sealed class WebhookDelivery : AggregateRoot
{
    private WebhookDelivery() { }

    private WebhookDelivery(
        Guid id,
        Guid subscriptionId,
        Guid eventId,
        string eventType,
        string payload,
        DateTimeOffset now)
        : base(id)
    {
        SubscriptionId = subscriptionId;
        EventId = eventId;
        EventType = eventType;
        Payload = payload;
        Status = WebhookDeliveryStatus.Pending;
        NextAttemptAt = now;
        CreatedAt = now;
    }

    public Guid SubscriptionId { get; private set; }

    /// <summary>
    /// The Platform's own id for the event.
    /// <para>
    /// Sent in a header so the subscriber can recognise a repeat. Delivery is
    /// at-least-once: a response lost on the way back is indistinguishable from
    /// one that never arrived, so the Platform tries again and the subscriber
    /// needs something to deduplicate on.
    /// </para>
    /// </summary>
    public Guid EventId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public WebhookDeliveryStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    /// <summary>What the subscriber answered, when it answered at all.</summary>
    public int? ResponseStatusCode { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public static WebhookDelivery Queue(
        Guid subscriptionId, Guid eventId, string eventType, string payload, DateTimeOffset now)
        => new(Guid.CreateVersion7(), subscriptionId, eventId, eventType, payload, now);

    public void RecordSuccess(int statusCode, DateTimeOffset now)
    {
        Attempts++;
        Status = WebhookDeliveryStatus.Delivered;
        ResponseStatusCode = statusCode;
        LastError = null;
        DeliveredAt = now;
    }

    /// <summary>
    /// Counts a failed attempt and schedules the next one, or gives up.
    /// </summary>
    /// <returns>Whether this attempt was the last.</returns>
    public bool RecordFailure(
        int? statusCode, string error, int maximumAttempts, TimeSpan backoff, DateTimeOffset now)
    {
        Attempts++;
        ResponseStatusCode = statusCode;

        // Truncated, because the body of a failure response is written by
        // somebody else's server and can be a megabyte of HTML.
        LastError = error.Length > 500 ? error[..500] : error;

        if (Attempts >= maximumAttempts)
        {
            Status = WebhookDeliveryStatus.Abandoned;

            return true;
        }

        NextAttemptAt = now + backoff;

        return false;
    }
}
