using System.Net.Mail;
using CCP.Kernel.Primitives;
using CCP.Modules.Integrations.Contracts;
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
    IOutboundGateway gateway,
    IOptions<EmailOptions> options,
    IClock clock,
    ILogger<EmailChannelProvider> logger) : INotificationChannelProvider
{
    private readonly EmailOptions _options = options.Value;

    public NotificationChannel Channel => NotificationChannel.Email;

    /// <summary>
    /// What the call log calls this channel.
    /// <para>
    /// A provider code, so mail appears beside the HTTP providers in the log and
    /// in the health view rather than in a category of its own that nobody
    /// thinks to look in.
    /// </para>
    /// </summary>
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

        // The door this channel spent nine phases walking past.
        //
        // The same allow-list every other outbound call passes, asked before the
        // socket rather than after it. A mail host nobody allowed is a host the
        // Platform will not reach, and a permanent refusal is right: retrying
        // a configuration fact for half an hour delays every message behind it.
        OutboundApproval approval =
            await gateway.ApproveHostAsync(_options.Host, cancellationToken);

        if (!approval.IsAllowed)
        {
            // The reason goes to the log and not to the caller. Learning which
            // check refused an address is how somebody maps a network one probe
            // at a time.
            logger.LogWarning(
                "The mail host {Host} is refused by the outbound policy: {Reason}.",
                _options.Host, approval.Reason);

            await RecordAsync(false, approval.Reason, TimeSpan.Zero, cancellationToken);

            return DeliveryOutcome.Permanent(
                "The mail host is not on the outbound allow-list.");
        }

        DateTimeOffset started = clock.UtcNow;

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

            await RecordAsync(true, null, clock.UtcNow - started, cancellationToken);

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

            await RecordAsync(false, exception.Message, clock.UtcNow - started, cancellationToken);

            return DeliveryOutcome.Permanent(exception.Message);
        }
        catch (FormatException exception)
        {
            // A malformed address. Also permanent, and worth distinguishing:
            // this one is the Platform's data being wrong rather than the
            // server's opinion.
            await RecordAsync(false, exception.Message, clock.UtcNow - started, cancellationToken);

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

            await RecordAsync(false, exception.Message, clock.UtcNow - started, cancellationToken);

            return DeliveryOutcome.Transient(exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The Platform is shutting down, not the mail server failing. The
            // notification stays pending and the next process picks it up.
            return DeliveryOutcome.Transient("Cancelled during shutdown.");
        }
    }

    /// <summary>
    /// One row in the shared call log, so "what has the Platform been sending,
    /// and did it arrive" has one answer covering every channel.
    /// <para>
    /// <b>The host, never the recipient.</b> The call log is read by
    /// administrators and exported; a list of who was emailed is not theirs to
    /// browse, and it would be the one place in the Platform where that list
    /// existed.
    /// </para>
    /// <para>
    /// A failure to write the row never fails the send. The message reaching
    /// somebody matters more than the Platform's record of it, and the shutdown
    /// path writes nothing at all: the Platform stopping is not a fact about the
    /// mail server.
    /// </para>
    /// </summary>
    private async Task RecordAsync(
        bool succeeded, string? detail, TimeSpan duration, CancellationToken cancellationToken)
    {
        try
        {
            await gateway.RecordAttemptAsync(
                new OutboundAttempt(
                    Name, "send", _options.Host, succeeded, detail, duration.TotalMilliseconds),
                cancellationToken);
        }
#pragma warning disable CA1031 // A log row is not worth losing a delivery over.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            logger.LogWarning(exception, "The outbound call log entry could not be written.");
        }
    }
}
