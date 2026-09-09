using System.Net.Mail;
using CCP.Modules.Notifications.Application.Abstractions;
using CCP.Modules.Notifications.Domain.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Notifications.Infrastructure.Channels;

/// <summary>
/// The Platform's own inbox.
/// <para>
/// <b>It cannot fail, and that is the point.</b> The row is already written by
/// the time the dispatcher sees it, so delivery is the act of saying so. Every
/// other channel depends on somebody else's server being up; this one gives a
/// person somewhere to look when none of them were.
/// </para>
/// </summary>
public sealed class InAppChannelProvider : INotificationChannelProvider
{
    public NotificationChannel Channel => NotificationChannel.InApp;

    public string Name => "in-app";

    public Task<DeliveryOutcome> SendAsync(
        Notification notification,
        CancellationToken cancellationToken = default)
        => Task.FromResult(DeliveryOutcome.Delivered());
}

/// <summary>Where to find a mail server, and who to send as.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Notifications:Email";

    /// <summary>
    /// Whether to attempt delivery at all.
    /// <para>
    /// Off by default. A Platform that starts talking to a mail server the
    /// moment it boots is one that emails real people from somebody's laptop,
    /// and enabling an outbound channel is the owner's decision (§19.1).
    /// </para>
    /// </summary>
    public bool Enabled { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public bool UseStartTls { get; set; } = true;

    /// <summary>
    /// The address messages come from.
    /// <para>
    /// Required when enabled: a message with no sender is refused by most
    /// servers and marked as spam by the rest.
    /// </para>
    /// </summary>
    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = string.Empty;

    /// <summary>
    /// Credentials, supplied by the environment and never by a file in the
    /// repository (§12.7). Empty means an unauthenticated relay, which is
    /// normal inside a company network.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);
}

/// <summary>
/// Email, over SMTP.
/// <para>
/// A direct adapter until Phase 12 gives outbound calls a governed path
/// (ARCHITECTURE.md §17.2). Deliberately thin: it renders no policy, holds no
/// retry logic and decides nothing about what happens after a failure — all of
/// which belong to the dispatcher, so that adding a second provider does not
/// mean re-implementing them.
/// </para>
/// <para>
/// It classifies failures, and only that. An address the server refuses is
/// permanent; a timeout is not. Getting that distinction wrong in either
/// direction is expensive: retrying a dead address for half an hour delays
/// every message behind it, and abandoning a timeout loses a message because a
/// server was busy for a second.
/// </para>
/// </summary>
public sealed class EmailChannelProvider(
    IRecipientDirectory recipients,
    IOptions<EmailOptions> options,
    ILogger<EmailChannelProvider> logger) : INotificationChannelProvider
{
    private readonly EmailOptions _options = options.Value;

    public NotificationChannel Channel => NotificationChannel.Email;

    public string Name => "smtp";

    public async Task<DeliveryOutcome> SendAsync(
        Notification notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (!_options.Enabled)
        {
            // Permanent, not transient. Retrying a channel that is switched off
            // would burn every attempt on a configuration fact and then report
            // it as a delivery failure half an hour later.
            return DeliveryOutcome.Permanent("The email channel is not enabled.");
        }

        if (string.IsNullOrWhiteSpace(_options.Host)
            || string.IsNullOrWhiteSpace(_options.FromAddress))
        {
            return DeliveryOutcome.Permanent("The email channel is enabled but not configured.");
        }

        string? address = await recipients.GetAddressAsync(
            notification.RecipientUserId, NotificationChannel.Email, cancellationToken);

        if (string.IsNullOrWhiteSpace(address))
        {
            return DeliveryOutcome.Permanent("The recipient has no email address.");
        }

        try
        {
            using var client = new SmtpClient(_options.Host, _options.Port)
            {
                EnableSsl = _options.UseStartTls,
                Timeout = (int)_options.Timeout.TotalMilliseconds
            };

            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                client.Credentials = new System.Net.NetworkCredential(
                    _options.Username, _options.Password);
            }

            using var message = new MailMessage
            {
                From = new MailAddress(_options.FromAddress, _options.FromName),
                Subject = notification.Subject,
                Body = notification.Body,

                // The body is HTML because templates escape their values and may
                // contain markup an administrator wrote. Sending it as plain
                // text would show people `&amp;` where an ampersand belongs.
                IsBodyHtml = true
            };

            message.To.Add(address);

            await client.SendMailAsync(message, cancellationToken);

            return DeliveryOutcome.Delivered($"Accepted by {_options.Host}.");
        }
        catch (SmtpFailedRecipientException exception)
        {
            // The server named the recipient as the problem. Trying again
            // changes nothing.
            logger.LogWarning(
                exception,
                "Mail server refused the recipient of notification {Id}.",
                notification.Id);

            return DeliveryOutcome.Permanent(exception.Message);
        }
        catch (FormatException exception)
        {
            // A malformed address. Also permanent, and worth distinguishing:
            // this one is the Platform's data being wrong rather than the
            // server's opinion.
            return DeliveryOutcome.Permanent(exception.Message);
        }
        catch (SmtpException exception)
        {
            // Everything else the protocol can go wrong with — a busy server, a
            // dropped connection, a restart. Worth another try.
            logger.LogWarning(
                exception,
                "Mail server was unavailable for notification {Id}. It will be retried.",
                notification.Id);

            return DeliveryOutcome.Transient(exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The Platform is shutting down, not the mail server failing. The
            // notification stays pending and the next process picks it up.
            return DeliveryOutcome.Transient("Cancelled during shutdown.");
        }
    }
}
