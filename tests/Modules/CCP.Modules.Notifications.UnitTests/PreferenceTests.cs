using CCP.Kernel.Results;
using CCP.Modules.Notifications.Domain.Notifications;
using CCP.Modules.Notifications.Domain.Preferences;

namespace CCP.Modules.Notifications.UnitTests;

/// <summary>
/// What a person can and cannot turn off.
/// <para>
/// <b>Security notifications cannot be disabled</b> is Phase 9's acceptance
/// criterion and the one rule here with a real adversary: the person most likely
/// to want "your password was changed" silenced is whoever changed it.
/// </para>
/// </summary>
public sealed class PreferenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.CreateVersion7();

    [Fact]
    public void NoPreferenceMeansEverythingIsAllowed()
    {
        // Absence means yes, so a new employee receives everything without
        // anybody generating a row per category per channel for them.
        Assert.True(NotificationPreference.IsAllowed("workflow", NotificationChannel.Email, []));
    }

    [Fact]
    public void ADisabledPreferenceStopsThatChannelOnly()
    {
        NotificationPreference off = Preference("workflow", NotificationChannel.Email);

        off.Set(false, Now);

        Assert.False(NotificationPreference.IsAllowed("workflow", NotificationChannel.Email, [off]));

        // The in-app copy still arrives. Turning off email is not turning off
        // the message.
        Assert.True(NotificationPreference.IsAllowed("workflow", NotificationChannel.InApp, [off]));
    }

    [Fact]
    public void SecurityCannotBeDisabledByAStoredRow()
    {
        // Constructed by hand to prove the resolver, not the constructor: even
        // if a row somehow existed — a migration, a direct write — the answer is
        // still yes.
        NotificationPreference smuggled = Preference("workflow", NotificationChannel.Email);

        smuggled.Set(false, Now);

        Assert.True(NotificationPreference.IsAllowed(
            NotificationPreference.SecurityCategory, NotificationChannel.Email, [smuggled]));
    }

    [Fact]
    public void ASecurityPreferenceCannotEvenBeCreated()
    {
        Result<NotificationPreference> created = NotificationPreference.Create(
            User, "security", NotificationChannel.Email, Now);

        // Refused at the point of creation, so no row can exist that a future
        // reader of the resolver might respect by accident.
        Assert.True(created.IsFailure);
        Assert.Equal("NOTIFICATIONS.SECURITY_CANNOT_BE_DISABLED", created.Errors[0].Code);
    }

    [Fact]
    public void TheSecurityCheckIgnoresCase()
    {
        Assert.True(NotificationPreference.IsAllowed("SECURITY", NotificationChannel.Email, []));

        Assert.True(NotificationPreference.Create(
            User, "Security", NotificationChannel.Email, Now).IsFailure);
    }

    [Fact]
    public void APreferenceCanBeTurnedBackOn()
    {
        NotificationPreference preference = Preference("workflow", NotificationChannel.Email);

        preference.Set(false, Now);
        preference.Set(true, Now);

        // The row stays rather than being deleted, so the trail shows somebody
        // turned this off in March and on again in June.
        Assert.True(preference.IsEnabled);
        Assert.True(NotificationPreference.IsAllowed(
            "workflow", NotificationChannel.Email, [preference]));
    }

    private static NotificationPreference Preference(string category, NotificationChannel channel)
        => NotificationPreference.Create(User, category, channel, Now).Value;
}

/// <summary>The delivery record, which is what makes a failure investigable.</summary>
public sealed class NotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Recipient = Guid.CreateVersion7();

    [Fact]
    public void EveryAttemptIsRecorded()
    {
        Notification notification = Pending();

        notification.RecordAttempt(false, "smtp", "busy", TimeSpan.FromMilliseconds(90), Now);
        notification.RecordAttempt(false, "smtp", "busy", TimeSpan.FromMilliseconds(80), Now);
        notification.RecordAttempt(true, "smtp", "accepted", TimeSpan.FromMilliseconds(40), Now);

        // "It failed twice and then worked" and "it worked first time" are
        // different facts, and only the first says the provider is unhealthy.
        Assert.Equal(3, notification.Deliveries.Count);
        Assert.Equal([1, 2, 3], notification.Deliveries.Select(d => d.Attempt));
        Assert.Equal(NotificationStatus.Delivered, notification.Status);
    }

    [Fact]
    public void AProviderResponseIsBounded()
    {
        Notification notification = Pending();

        notification.RecordAttempt(
            false, "smtp", new string('x', 5000), TimeSpan.Zero, Now);

        // A provider response is somebody else's text. A server returning a
        // megabyte of diagnostics must not be able to make a row unreadable.
        Assert.Equal(1000, notification.Deliveries[0].ProviderResponse!.Length);
    }

    [Fact]
    public void ADeliveredNotificationCannotBeAbandoned()
    {
        Notification notification = Pending();

        notification.RecordAttempt(true, "in-app", null, TimeSpan.Zero, Now);

        Result abandoned = notification.Abandon(Now);

        Assert.True(abandoned.IsFailure);
        Assert.Equal(NotificationStatus.Delivered, notification.Status);
    }

    [Fact]
    public void OnlyTheRecipientCanMarkItRead()
    {
        Notification notification = Pending();

        Result stranger = notification.MarkRead(Guid.CreateVersion7(), Now);

        Assert.True(stranger.IsFailure);
        Assert.Equal("NOTIFICATIONS.NOT_THE_RECIPIENT", stranger.Errors[0].Code);
        Assert.Null(notification.ReadAt);

        Assert.True(notification.MarkRead(Recipient, Now).IsSuccess);
        Assert.Equal(Now, notification.ReadAt);
    }

    [Fact]
    public void ReadingTwiceKeepsTheFirstTime()
    {
        Notification notification = Pending();

        notification.MarkRead(Recipient, Now);
        notification.MarkRead(Recipient, Now.AddHours(1));

        // When they first saw it, not when they last opened it.
        Assert.Equal(Now, notification.ReadAt);
    }

    private static Notification Pending()
        => Notification.Create(
            Recipient, "workflow", NotificationChannel.InApp, "en",
            "Subject", "Body", "code", 1, Now).Value;
}
