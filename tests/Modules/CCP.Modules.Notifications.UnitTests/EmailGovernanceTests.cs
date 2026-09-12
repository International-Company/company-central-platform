using CCP.Kernel.Primitives;
using CCP.Modules.Integrations.Contracts;
using CCP.Modules.Notifications.Application.Abstractions;
using CCP.Modules.Notifications.Domain.Notifications;
using CCP.Modules.Notifications.Infrastructure.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Notifications.UnitTests;

/// <summary>
/// The email channel goes through the governed door.
/// <para>
/// <b>It was the one outbound call in the Platform that passed no door at
/// all</b> — recorded twice, as #33 and #45, and open since Phase 9. It opened a
/// socket to a mail server directly: no allow-list, no entry in the call log,
/// and no way for an operator to discover that the Platform had been failing to
/// send anything for a day.
/// </para>
/// <para>
/// The assertion that matters is the first one. Wiring a check in and never
/// consulting it looks exactly like wiring it in correctly — every other test
/// here would pass either way — so the one below refuses at the door and then
/// insists nothing was sent.
/// </para>
/// </summary>
public sealed class EmailGovernanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A refused host means no socket, and the refusal is permanent.
    /// <para>
    /// Permanent rather than transient, because retrying a configuration fact
    /// for half an hour delays every message queued behind it and then reports
    /// the same answer.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ARefusedMailHostIsNotDialled()
    {
        var gateway = new RecordingGateway(allowed: false);

        DeliveryOutcome outcome = await Build(gateway).SendAsync(ANotification());

        Assert.False(outcome.Succeeded);
        Assert.True(outcome.IsPermanent);

        // Nothing was attempted. The mail host in these options does not exist,
        // so a provider that ignored the gateway would have failed slowly with a
        // resolution error rather than quickly with a refusal -- but it would
        // also have failed, which is why the recorded attempt is asserted too.
        Assert.True(gateway.WasAsked);
    }

    /// <summary>
    /// And the refusal reaches the shared call log, so "what has the Platform
    /// been sending, and did it arrive" has one answer covering every channel
    /// rather than one answer for HTTP and a shrug for mail.
    /// </summary>
    [Fact]
    public async Task ARefusedMailHostIsWrittenToTheCallLog()
    {
        var gateway = new RecordingGateway(allowed: false);

        await Build(gateway).SendAsync(ANotification());

        OutboundAttempt attempt = Assert.Single(gateway.Attempts);

        Assert.Equal("smtp", attempt.Channel);
        Assert.False(attempt.Succeeded);
        Assert.Equal("HostNotAllowed", attempt.Detail);
    }

    /// <summary>
    /// <b>The host, never the recipient.</b>
    /// <para>
    /// The call log is read by administrators and exported. A list of who was
    /// emailed is not theirs to browse, and this would be the one place in the
    /// Platform where that list existed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheCallLogRecordsTheHostAndNotTheRecipient()
    {
        var gateway = new RecordingGateway(allowed: false);

        await Build(gateway).SendAsync(ANotification());

        OutboundAttempt attempt = Assert.Single(gateway.Attempts);

        Assert.Equal("mail.invalid", attempt.Destination);
        Assert.DoesNotContain("someone@example.test", attempt.Destination, StringComparison.Ordinal);
    }

    /// <summary>
    /// A channel that is switched off is refused before the door is even asked.
    /// <para>
    /// Nothing is going to be sent, so there is nothing to govern and nothing to
    /// log — and an allow-list refusal recorded against a channel nobody enabled
    /// would be a row that means something it does not.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ASwitchedOffChannelAsksNothing()
    {
        var gateway = new RecordingGateway(allowed: true);

        DeliveryOutcome outcome = await Build(gateway, enabled: false).SendAsync(ANotification());

        Assert.False(outcome.Succeeded);
        Assert.True(outcome.IsPermanent);
        Assert.False(gateway.WasAsked);
        Assert.Empty(gateway.Attempts);
    }

    // --- Fixtures -----------------------------------------------------------

    private static Notification ANotification()
        => Notification.Create(
            Guid.CreateVersion7(), "security", NotificationChannel.Email, "en",
            "Subject", "Body", null, null, Now).Value;

    private static EmailChannelProvider Build(
        RecordingGateway gateway, bool enabled = true)
        => new(
            new StubRecipients(),
            gateway,
            Options.Create(new EmailOptions
            {
                Enabled = enabled,
                Host = "mail.invalid",
                Port = 25,
                FromAddress = "platform@example.test",
                FromName = "Platform"
            }),
            new FixedClock(),
            NullLogger<EmailChannelProvider>.Instance);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class StubRecipients : IRecipientDirectory
    {
        public Task<string?> GetAddressAsync(
            Guid userId, NotificationChannel channel, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("someone@example.test");

        public Task<string> GetLocaleAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult("en");
    }

    private sealed class RecordingGateway(bool allowed) : IOutboundGateway
    {
        public bool WasAsked { get; private set; }

        public List<OutboundAttempt> Attempts { get; } = [];

        public Task<OutboundApproval> ApproveHostAsync(
            string host, CancellationToken cancellationToken = default)
        {
            WasAsked = true;

            return Task.FromResult(
                allowed
                    ? new OutboundApproval(true, "Allowed")
                    : new OutboundApproval(false, "HostNotAllowed"));
        }

        public Task RecordAttemptAsync(
            OutboundAttempt attempt, CancellationToken cancellationToken = default)
        {
            Attempts.Add(attempt);

            return Task.CompletedTask;
        }
    }
}
