using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;

namespace CCP.Modules.Notifications.Domain.Notifications;

/// <summary>
/// One message to one person, on one channel.
/// <para>
/// A notification is created already rendered. The template it came from may be
/// revised or deactivated afterwards, and a message whose text is resolved at
/// delivery time would change after it was composed — so what was sent is stored,
/// not a pointer to what the text is now.
/// </para>
/// </summary>
public sealed class Notification : AggregateRoot, IAuditableEntity
{
    private readonly List<NotificationDelivery> _deliveries = [];

    private Notification() { }

    private Notification(
        Guid id,
        Guid recipientUserId,
        string category,
        NotificationChannel channel,
        string locale,
        string subject,
        string body,
        string? templateCode,
        int? templateVersion,
        DateTimeOffset now)
        : base(id)
    {
        RecipientUserId = recipientUserId;
        Category = category;
        Channel = channel;
        Locale = locale;
        Subject = subject;
        Body = body;
        TemplateCode = templateCode;
        TemplateVersion = templateVersion;
        Status = NotificationStatus.Pending;
        CreatedAt = now;
    }

    public Guid RecipientUserId { get; private set; }

    /// <summary>
    /// What kind of message this is — the unit a preference switches off.
    /// <para>
    /// A string rather than an enum, because categories arrive with the
    /// applications that send them and the Platform has never heard of most of
    /// them. <c>security</c> is the one it does know, and the one nobody can
    /// disable.
    /// </para>
    /// </summary>
    public string Category { get; private set; } = string.Empty;

    public NotificationChannel Channel { get; private set; }

    public string Locale { get; private set; } = string.Empty;

    public string Subject { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    /// <summary>Which template produced this, for the delivery record.</summary>
    public string? TemplateCode { get; private set; }

    /// <summary>
    /// Which version of it.
    /// <para>
    /// Recorded so "what did we actually send them?" has an answer after the
    /// template has been revised twice.
    /// </para>
    /// </summary>
    public int? TemplateVersion { get; private set; }

    public NotificationStatus Status { get; private set; }

    /// <summary>When the recipient read it. In-app only.</summary>
    public DateTimeOffset? ReadAt { get; private set; }

    public IReadOnlyList<NotificationDelivery> Deliveries => _deliveries.AsReadOnly();

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static Result<Notification> Create(
        Guid recipientUserId,
        string category,
        NotificationChannel channel,
        string locale,
        string subject,
        string body,
        string? templateCode,
        int? templateVersion,
        DateTimeOffset now)
    {
        if (recipientUserId == Guid.Empty)
        {
            return Result.Failure<Notification>(NotificationErrors.RecipientRequired);
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            return Result.Failure<Notification>(NotificationErrors.CategoryRequired);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return Result.Failure<Notification>(NotificationErrors.BodyRequired);
        }

        return Result.Success(new Notification(
            Uuid7.NewGuid(now), recipientUserId, category.Trim().ToLowerInvariant(),
            channel, locale, subject.Trim(), body.Trim(), templateCode, templateVersion, now));
    }

    /// <summary>
    /// Records an attempt to deliver, successful or not.
    /// <para>
    /// <b>Every attempt, not just the last.</b> "It failed three times and then
    /// worked" and "it worked first time" are different facts, and only the first
    /// tells anybody the provider is unhealthy.
    /// </para>
    /// </summary>
    public NotificationDelivery RecordAttempt(
        bool succeeded,
        string? providerName,
        string? providerResponse,
        TimeSpan duration,
        DateTimeOffset now)
    {
        var attempt = NotificationDelivery.Record(
            Id, _deliveries.Count + 1, succeeded, providerName,
            Truncate(providerResponse), duration, now);

        _deliveries.Add(attempt);

        if (succeeded)
        {
            Status = NotificationStatus.Delivered;
        }

        UpdatedAt = now;

        return attempt;
    }

    /// <summary>
    /// Gives up.
    /// <para>
    /// A permanently failed notification stays visible in administration rather
    /// than disappearing (§17.4). A message nobody received and nobody can see
    /// was never sent, and the person who needed it will find out some other
    /// way — usually badly.
    /// </para>
    /// </summary>
    public Result Abandon(DateTimeOffset now)
    {
        if (Status == NotificationStatus.Delivered)
        {
            return Result.Failure(NotificationErrors.AlreadyDelivered);
        }

        Status = NotificationStatus.Failed;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>Marks an in-app notification as read by its recipient.</summary>
    public Result MarkRead(Guid readerUserId, DateTimeOffset now)
    {
        if (readerUserId != RecipientUserId)
        {
            // One person's inbox is not another's. Checked here as well as in
            // the handler, because reading somebody else's messages is the
            // failure this module most needs to make impossible.
            return Result.Failure(NotificationErrors.NotTheRecipient);
        }

        if (ReadAt is not null)
        {
            return Result.Success();
        }

        ReadAt = now;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Bounds what a provider can write into the record.
    /// <para>
    /// A provider response is somebody else's text. An SMTP server returning a
    /// megabyte of diagnostics should not be able to make a row unreadable or a
    /// table large.
    /// </para>
    /// </summary>
    private static string? Truncate(string? response)
        => response is null ? null
            : response.Length <= 1000 ? response
            : response[..1000];
}

/// <summary>Where a notification goes.</summary>
public enum NotificationChannel
{
    /// <summary>The Platform's own inbox. Always available, never fails.</summary>
    InApp = 1,

    Email = 2,

    /// <summary>Designed for and deliberately not built (§17.2, Q9).</summary>
    Sms = 3,

    /// <summary>Designed for and deliberately not built.</summary>
    Push = 4
}

/// <summary>How a notification is getting on.</summary>
public enum NotificationStatus
{
    /// <summary>Queued. Nothing has been attempted yet.</summary>
    Pending = 1,

    Delivered = 2,

    /// <summary>Attempts exhausted. Visible in administration, not deleted.</summary>
    Failed = 3
}

/// <summary>
/// One attempt to deliver one notification.
/// <para>
/// Append-only, like every other log in the Platform: an attempt that can be
/// edited is an attempt that proves nothing.
/// </para>
/// </summary>
public sealed class NotificationDelivery : Entity
{
    private NotificationDelivery() { }

    private NotificationDelivery(
        Guid id,
        Guid notificationId,
        int attempt,
        bool succeeded,
        string? providerName,
        string? providerResponse,
        TimeSpan duration,
        DateTimeOffset now)
        : base(id)
    {
        NotificationId = notificationId;
        Attempt = attempt;
        Succeeded = succeeded;
        ProviderName = providerName;
        ProviderResponse = providerResponse;
        DurationMs = (int)Math.Min(duration.TotalMilliseconds, int.MaxValue);
        OccurredAt = now;
    }

    public Guid NotificationId { get; private set; }

    /// <summary>1 for the first try. Counting makes a retry storm visible.</summary>
    public int Attempt { get; private set; }

    public bool Succeeded { get; private set; }

    /// <summary>Which provider handled it — the answer to "is SMTP broken?".</summary>
    public string? ProviderName { get; private set; }

    public string? ProviderResponse { get; private set; }

    /// <summary>How long it took. A provider getting slower is a warning.</summary>
    public int DurationMs { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    internal static NotificationDelivery Record(
        Guid notificationId,
        int attempt,
        bool succeeded,
        string? providerName,
        string? providerResponse,
        TimeSpan duration,
        DateTimeOffset now)
        => new(Uuid7.NewGuid(now), notificationId, attempt, succeeded,
            providerName, providerResponse, duration, now);
}
