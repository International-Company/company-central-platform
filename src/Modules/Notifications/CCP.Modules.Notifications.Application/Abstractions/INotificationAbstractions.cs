using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Application.Events;
using CCP.Modules.Notifications.Domain.Notifications;
using CCP.Modules.Notifications.Domain.Preferences;
using CCP.Modules.Notifications.Domain.Templates;

namespace CCP.Modules.Notifications.Application.Abstractions;

/// <summary>Commits the module's changes as one transaction.</summary>
public interface INotificationUnitOfWork : IUnitOfWork;

/// <summary>The module's slice of the transactional outbox.</summary>
public interface INotificationOutbox : IOutbox;

/// <summary>
/// Delivers a message on one channel.
/// <para>
/// <b>The seam that makes channels additive</b> (ARCHITECTURE.md §17.2). Adding
/// SMS later means writing one of these and registering it — no change to
/// dispatch, to templates, or to any caller. Replacing one email vendor with
/// another is a configuration change rather than a code change, which is the
/// difference between a provider you can leave and a provider you are married
/// to.
/// </para>
/// <para>
/// A provider reports failure; it does not decide what happens next. Retry,
/// backoff and giving up belong to the dispatcher, so every channel behaves the
/// same way when a vendor is down — and a provider written next year cannot
/// invent its own retry policy by accident.
/// </para>
/// </summary>
public interface INotificationChannelProvider
{
    /// <summary>Which channel this delivers on.</summary>
    NotificationChannel Channel { get; }

    /// <summary>
    /// A name for the delivery log, so "which provider handled it" has an
    /// answer when two are configured for one channel over time.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Attempts delivery.
    /// <para>
    /// Must not throw for an ordinary failure — a refused address, a rejecting
    /// server. Those are results, and a dispatcher that learned about them
    /// through exceptions would treat "this address does not exist" the same as
    /// "the process is broken".
    /// </para>
    /// </summary>
    Task<DeliveryOutcome> SendAsync(
        Notification notification,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What a provider made of an attempt.
/// </summary>
/// <param name="Succeeded">Whether it was accepted for delivery.</param>
/// <param name="Response">
/// What the provider said, for the log. Truncated before storage: it is somebody
/// else's text and must not be able to make a row unreadable.
/// </param>
/// <param name="IsPermanent">
/// Whether retrying is pointless. A malformed address stays malformed however
/// many times it is tried, and retrying it for an hour delays every message
/// behind it while achieving nothing.
/// </param>
public sealed record DeliveryOutcome(bool Succeeded, string? Response, bool IsPermanent = false)
{
    public static DeliveryOutcome Delivered(string? response = null) => new(true, response);

    /// <summary>A failure worth trying again — a timeout, a busy server.</summary>
    public static DeliveryOutcome Transient(string? response) => new(false, response);

    /// <summary>A failure that will not change — a rejected address.</summary>
    public static DeliveryOutcome Permanent(string? response) => new(false, response, true);
}

/// <summary>
/// Where a person's address on a channel comes from.
/// <para>
/// Implemented outside this module. Notifications knows how to render and
/// deliver; it does not know what an email address is or where the Platform
/// keeps one, and a module that reached into Identity's schema to find out
/// would not be extractable (§6.2).
/// </para>
/// </summary>
public interface IRecipientDirectory
{
    /// <summary>
    /// The address to use on this channel, or null when there is none.
    /// <para>
    /// Null is normal: a service account has no inbox, and a contractor may
    /// have no email on file. The dispatcher treats it as "cannot deliver here"
    /// rather than as an error.
    /// </para>
    /// </summary>
    Task<string?> GetAddressAsync(
        Guid userId,
        NotificationChannel channel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The language to write to this person in.
    /// <para>
    /// Falls back to the company default rather than to English. A Platform
    /// deployed in Arabic should not send English to somebody who never chose
    /// one.
    /// </para>
    /// </summary>
    Task<string> GetLocaleAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes the module's aggregates.</summary>
public interface INotificationRepository
{
    // --- Templates ----------------------------------------------------------

    Task<NotificationTemplate?> FindTemplateAsync(
        string code, string locale, CancellationToken cancellationToken = default);

    Task<NotificationTemplate?> FindTemplateByIdAsync(
        Guid templateId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationTemplate>> GetTemplatesAsync(
        bool includeInactive, CancellationToken cancellationToken = default);

    void AddTemplate(NotificationTemplate template);

    // --- Notifications ------------------------------------------------------

    Task<Notification?> FindNotificationAsync(
        Guid notificationId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Notification> Items, long Total)> GetInboxAsync(
        Guid userId, bool unreadOnly, int skip, int take,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Notification> Items, long Total)> SearchAsync(
        NotificationStatus? status, string? category, NotificationChannel? channel,
        int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>Pending notifications the dispatcher should attempt.</summary>
    Task<IReadOnlyList<Notification>> GetPendingAsync(
        int limit, CancellationToken cancellationToken = default);

    void AddNotification(Notification notification);

    // --- Preferences --------------------------------------------------------

    Task<IReadOnlyList<NotificationPreference>> GetPreferencesAsync(
        Guid userId, CancellationToken cancellationToken = default);

    Task<NotificationPreference?> FindPreferenceAsync(
        Guid userId, string category, NotificationChannel channel,
        CancellationToken cancellationToken = default);

    void AddPreference(NotificationPreference preference);
}
