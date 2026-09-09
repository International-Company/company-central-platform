using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Notifications.Domain.Notifications;

namespace CCP.Modules.Notifications.Domain.Preferences;

/// <summary>
/// One person's choice about one kind of message on one channel.
/// <para>
/// Absence means yes. A row exists only when somebody has turned something
/// off, so a new employee receives everything without anybody generating a
/// preference row per category per channel for them — and a category invented
/// next year works for existing users without a migration.
/// </para>
/// </summary>
public sealed class NotificationPreference : AggregateRoot, IAuditableEntity
{
    /// <summary>
    /// The category no preference can silence.
    /// <para>
    /// A password changed, a second factor removed, a sign-in from a new place:
    /// these tell somebody their account may have been taken, and the person
    /// best placed to want them off is whoever took it. Not a policy choice —
    /// a structural one, enforced in the resolver rather than in a document.
    /// </para>
    /// </summary>
    public const string SecurityCategory = "security";

    private NotificationPreference() { }

    private NotificationPreference(
        Guid id,
        Guid userId,
        string category,
        NotificationChannel channel,
        DateTimeOffset now)
        : base(id)
    {
        UserId = userId;
        Category = category;
        Channel = channel;
        IsEnabled = false;
        CreatedAt = now;
    }

    public Guid UserId { get; private set; }

    public string Category { get; private set; } = string.Empty;

    public NotificationChannel Channel { get; private set; }

    /// <summary>
    /// Whether this combination is wanted.
    /// <para>
    /// A row exists to say "no". It can be flipped back to yes, which leaves the
    /// row behind rather than deleting it — so the trail shows that somebody
    /// turned this off in March and on again in June.
    /// </para>
    /// </summary>
    public bool IsEnabled { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static Result<NotificationPreference> Create(
        Guid userId,
        string category,
        NotificationChannel channel,
        DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<NotificationPreference>(NotificationErrors.RecipientRequired);
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            return Result.Failure<NotificationPreference>(NotificationErrors.CategoryRequired);
        }

        string normalised = category.Trim().ToLowerInvariant();

        if (normalised == SecurityCategory)
        {
            // Refused at the point of creation, so no row can exist that a
            // future reader of the resolver might respect by accident.
            return Result.Failure<NotificationPreference>(
                NotificationErrors.SecurityNotificationsCannotBeDisabled);
        }

        return Result.Success(new NotificationPreference(
            Uuid7.NewGuid(now), userId, normalised, channel, now));
    }

    public Result Set(bool isEnabled, DateTimeOffset now)
    {
        IsEnabled = isEnabled;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Whether this category may be sent on this channel, given what is stored.
    /// <para>
    /// The whole preference model in one method, and the security exemption is
    /// the first line of it — checked before anything is looked up, so no
    /// stored row and no missing row can change the answer.
    /// </para>
    /// </summary>
    public static bool IsAllowed(
        string category,
        NotificationChannel channel,
        IReadOnlyList<NotificationPreference> stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        if (string.Equals(category, SecurityCategory, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        NotificationPreference? preference = stored.FirstOrDefault(
            p => string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase)
              && p.Channel == channel);

        // No row means nobody said no.
        return preference?.IsEnabled ?? true;
    }
}
