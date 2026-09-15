using System.Globalization;
using CCP.Kernel.Application.Events;
using CCP.Modules.Identity.Contracts.Events;
using CCP.Modules.Notifications.Application.Sending;
using CCP.Modules.Notifications.Domain.Notifications;
using CCP.Modules.Notifications.Domain.Preferences;
using CCP.Modules.Security.Contracts.Events;
using Microsoft.Extensions.Logging;

namespace CCP.Modules.Notifications.Infrastructure.Listeners;

/// <summary>
/// Sends the reset link, and closes the oldest debt in this project.
/// <para>
/// <b>Password reset has been staged on the outbox since Phase 2 with nothing
/// to deliver it.</b> The token was issued, the event was written, and it sat
/// there — so the flow existed, was tested, and could not complete. This is the
/// listener that finishes it.
/// </para>
/// <para>
/// <b>Email only, deliberately.</b> An in-app copy would put a working
/// account-takeover credential in the inbox of the account it takes over, which
/// is useless to somebody locked out and useful to whoever locked them out.
/// </para>
/// <para>
/// The token reaches this listener in the event payload, which is why the audit
/// trail redacts it and why processed outbox rows are worth pruning
/// (ARCHITECTURE.md §15.7). Short-lived, so the window is bounded — but a reset
/// token is a password, and every place it is written down is a place it can be
/// read.
/// </para>
/// </summary>
public sealed class PasswordResetRequestedListener(
    NotificationSender sender,
    ILogger<PasswordResetRequestedListener> logger)
    : IIntegrationEventHandler<PasswordResetRequestedEvent>
{
    public async Task HandleAsync(
        PasswordResetRequestedEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        int minutes = Math.Max(
            1,
            (int)(integrationEvent.ExpiresAt - integrationEvent.At).TotalMinutes);

        var result = await sender.SendAsync(
            new SendRequest(
                integrationEvent.UserId,
                "security.password.reset",
                NotificationPreference.SecurityCategory,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["token"] = integrationEvent.Token,
                    ["validForMinutes"] = minutes.ToString(CultureInfo.InvariantCulture)
                },
                [NotificationChannel.Email],
                CausedBy: integrationEvent.EventId),
            cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning(
                "Could not send a password reset to {User}: {Error}",
                integrationEvent.UserId,
                result.Errors[0].Message);
        }
    }
}

/// <summary>
/// Tells somebody their password changed.
/// <para>
/// The message that matters when it was not them. It says what happened and
/// what to do, because "a security event occurred on your account" leaves a
/// worried person with nowhere to go.
/// </para>
/// <para>
/// Category <c>security</c>, so no preference can silence it: the person most
/// likely to want this off is whoever changed the password.
/// </para>
/// </summary>
public sealed class PasswordChangedListener(
    NotificationSender sender,
    ILogger<PasswordChangedListener> logger)
    : IIntegrationEventHandler<UserPasswordChangedEvent>
{
    public async Task HandleAsync(
        UserPasswordChangedEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var result = await sender.SendAsync(
            new SendRequest(
                integrationEvent.UserId,
                "security.password.changed",
                NotificationPreference.SecurityCategory,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["occurredAt"] = integrationEvent.At.ToString(
                        "d MMMM yyyy HH:mm", CultureInfo.InvariantCulture)
                },
                    Channels: null,
                    CausedBy: integrationEvent.EventId),
            cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning(
                "Could not notify {User} that their password changed: {Error}",
                integrationEvent.UserId,
                result.Errors[0].Message);
        }
    }
}

/// <summary>
/// Tells somebody that two-factor authentication was turned on for their account.
/// <para>
/// <b>The template for this was seeded and never sent.</b> Security published no
/// integration events at all, so nothing outside it learned that an enrolment
/// happened. It is the notification that exposes a takeover: an intruder with a
/// stolen session enrols an authenticator of their own to keep access after the
/// password is reset, and the real owner is who needs to know.
/// </para>
/// <para>
/// In the security category, like the password-changed notice, so it is not
/// something a person can switch off and then fail to hear about.
/// </para>
/// </summary>
public sealed class MfaEnrolledListener(
    NotificationSender sender,
    ILogger<MfaEnrolledListener> logger)
    : IIntegrationEventHandler<MfaEnrolledEvent>
{
    public async Task HandleAsync(
        MfaEnrolledEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var result = await sender.SendAsync(
            new SendRequest(
                integrationEvent.UserId,
                "security.mfa.enrolled",
                NotificationPreference.SecurityCategory,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["occurredAt"] = integrationEvent.At.ToString(
                        "d MMMM yyyy HH:mm", CultureInfo.InvariantCulture)
                },
                    Channels: null,
                    CausedBy: integrationEvent.EventId),
            cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning(
                "Could not notify {User} that two-factor authentication was turned on: {Error}",
                integrationEvent.UserId,
                result.Errors[0].Message);
        }
    }
}
