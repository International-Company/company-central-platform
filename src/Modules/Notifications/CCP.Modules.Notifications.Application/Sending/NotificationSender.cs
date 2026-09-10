using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Notifications.Application.Abstractions;
using CCP.Modules.Notifications.Domain;
using CCP.Modules.Notifications.Domain.Notifications;
using CCP.Modules.Notifications.Domain.Preferences;
using CCP.Modules.Notifications.Domain.Templates;

namespace CCP.Modules.Notifications.Application.Sending;

/// <summary>What one caller wants said to one person.</summary>
/// <param name="CausedBy">
/// The integration event that prompted this, when there was one.
/// <para>
/// Outbox delivery is at-least-once by design -- a crash between dispatching a
/// message and marking it processed causes a redelivery, which is the right
/// trade because the alternative loses events. Supplying the event id here is
/// what stops the second delivery producing a second copy in somebody's inbox.
/// </para>
/// <para>
/// Null for a send that no event caused, which is every send made directly by a
/// handler. Those are not redelivered and have nothing to deduplicate against.
/// </para>
/// </param>
public sealed record SendRequest(
    Guid RecipientUserId,
    string TemplateCode,
    string Category,
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlyList<NotificationChannel>? Channels = null,
    Guid? CausedBy = null);

/// <summary>
/// Turning "tell this person that" into notifications.
/// <para>
/// <b>Sending enqueues; it does not deliver</b> (§17.4). The caller's
/// transaction commits a row, and a background dispatcher does the slow,
/// failure-prone work of talking to a mail server. A send that blocked on SMTP
/// would make every action in the Platform as slow and as unreliable as the
/// slowest thing it notifies about.
/// </para>
/// <para>
/// Every decision that could go wrong happens here, before anything is queued:
/// the template must exist in the recipient's language, every variable it needs
/// must be supplied, and the person must not have turned this category off. A
/// notification reaching the queue is one that will be attempted.
/// </para>
/// </summary>
public sealed class NotificationSender(
    INotificationRepository repository,
    IRecipientDirectory recipients,
    INotificationUnitOfWork unitOfWork,
    IClock clock)
{
    /// <summary>
    /// The channels used when a caller does not say.
    /// <para>
    /// In-app always, because it cannot fail and gives the person somewhere to
    /// look. Email as well, because a message only in an inbox nobody has opened
    /// is a message nobody has read.
    /// </para>
    /// </summary>
    private static readonly NotificationChannel[] DefaultChannels =
        [NotificationChannel.InApp, NotificationChannel.Email];

    public async Task<Result<IReadOnlyList<Guid>>> SendAsync(
        SendRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset now = clock.UtcNow;

        // Asked first, and cheaply. A redelivered event has already produced
        // everything it was going to; going further would re-read the template,
        // re-resolve preferences and write a second copy of a message the
        // person has already read.
        //
        // Success rather than a failure: the caller asked for somebody to be
        // told, and somebody has been told. Reporting an error would make every
        // listener handle a case that is not a problem.
        if (request.CausedBy is { } eventId
            && await repository.HasProcessedAsync(eventId, request.TemplateCode, cancellationToken))
        {
            return Result.Success<IReadOnlyList<Guid>>([]);
        }

        string locale = await recipients.GetLocaleAsync(request.RecipientUserId, cancellationToken);

        NotificationTemplate? template = await repository.FindTemplateAsync(
            request.TemplateCode, locale, cancellationToken);

        if (template is null || !template.IsActive)
        {
            // Not substituted with another language. A director receiving an
            // approval request in the wrong language is a failure the company
            // sees, and a silent fallback is how that ships.
            return Result.Failure<IReadOnlyList<Guid>>(
                NotificationErrors.NoTemplateForLocale(request.TemplateCode, locale));
        }

        Result<RenderedMessage> rendered = template.Render(request.Variables);

        if (rendered.IsFailure)
        {
            // Refused before anything is queued. A missing variable discovered
            // at delivery time is a message already halfway to somebody.
            return Result.Failure<IReadOnlyList<Guid>>(rendered.Errors);
        }

        IReadOnlyList<NotificationPreference> preferences =
            await repository.GetPreferencesAsync(request.RecipientUserId, cancellationToken);

        var created = new List<Guid>();

        foreach (NotificationChannel channel in request.Channels ?? DefaultChannels)
        {
            if (!NotificationPreference.IsAllowed(request.Category, channel, preferences))
            {
                continue;
            }

            // No address, no delivery. A service account has no inbox and a
            // contractor may have no email on file; queueing a message that
            // cannot go anywhere would fill the failure log with a fact nobody
            // can act on.
            if (channel != NotificationChannel.InApp)
            {
                string? address = await recipients.GetAddressAsync(
                    request.RecipientUserId, channel, cancellationToken);

                if (string.IsNullOrWhiteSpace(address))
                {
                    continue;
                }
            }

            Result<Notification> notification = Notification.Create(
                request.RecipientUserId,
                request.Category,
                channel,
                locale,
                rendered.Value.Subject,
                rendered.Value.Body,
                template.Code,
                template.Version,
                now);

            if (notification.IsFailure)
            {
                return Result.Failure<IReadOnlyList<Guid>>(notification.Errors);
            }

            repository.AddNotification(notification.Value);
            created.Add(notification.Value.Id);
        }

        if (request.CausedBy is { } processedEventId)
        {
            // In this SaveChanges, not a separate one. A marker committed apart
            // from the notification leaves a window in which the notification
            // exists and the marker does not -- so a crash there still
            // duplicates, and the whole mechanism would be reassurance rather
            // than a guarantee.
            repository.MarkProcessed(
                ProcessedEvent.Record(processedEventId, request.TemplateCode, now));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // An empty list is a success, not a failure. Everything was turned off,
        // or nowhere to send it — both are the system working as configured, and
        // an error would make a caller handle a case that is not their problem.
        return Result.Success<IReadOnlyList<Guid>>(created);
    }
}
