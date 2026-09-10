namespace CCP.Modules.Notifications.Domain.Notifications;

/// <summary>
/// A note that one event has already produced its notification.
/// <para>
/// <b>Outbox delivery is at-least-once, by design.</b> A crash between
/// dispatching a message and marking it processed causes a redelivery, which is
/// the correct trade: the alternative loses events. The cost is that a listener
/// can be handed the same event twice, and until this existed the second time
/// produced a second "you have something to approve" in somebody's inbox.
/// </para>
/// <para>
/// <b>Written in the same transaction as the notification it describes.</b> That
/// is the whole point and the only arrangement that works. A marker committed
/// separately leaves a window in which the notification exists and the marker
/// does not — so a crash there still duplicates, and the mechanism would provide
/// reassurance rather than a guarantee.
/// </para>
/// <para>
/// <b>Keyed on the event and the reason together, not on the event alone.</b>
/// Two listeners may legitimately react to one event — a task being assigned
/// both notifies the assignee and, one day, updates a digest — and a key on the
/// event id alone would let whichever ran first silence the other for ever.
/// That is the failure this project was warned about in its own debt register:
/// a duplicate is a nuisance, and dropping the first copy through a bug in
/// deduplication is not.
/// </para>
/// </summary>
public sealed class ProcessedEvent
{
    private ProcessedEvent() { }

    /// <summary>The event's own identity, carried by every integration event.</summary>
    public Guid EventId { get; private init; }

    /// <summary>
    /// What consumed it — the template code the notification was sent from.
    /// <para>
    /// The template rather than the listener's class name: a class can be
    /// renamed and a template code cannot, because it is a key in a table of
    /// messages. A rename that silently re-sent every notification anybody had
    /// already received would be a memorable afternoon.
    /// </para>
    /// </summary>
    public string Reason { get; private init; } = string.Empty;

    public DateTimeOffset ProcessedAt { get; private init; }

    public static ProcessedEvent Record(Guid eventId, string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new ProcessedEvent
        {
            EventId = eventId,
            Reason = reason.Trim(),
            ProcessedAt = now
        };
    }
}
