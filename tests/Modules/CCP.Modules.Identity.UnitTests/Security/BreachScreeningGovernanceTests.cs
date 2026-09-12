using CCP.Kernel.Primitives;
using CCP.Modules.Identity.Infrastructure.Security;
using CCP.Modules.Integrations.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Identity.UnitTests.Security;

/// <summary>
/// Breached-password screening goes through the governed door.
/// <para>
/// <b>It was the second outbound call found going round it</b>, after the email
/// channel — and the second is what turned a defect into a class. Both were
/// written before the integration layer existed, both reached a third party on
/// the public internet, and both were found by reading rather than by anything
/// failing.
/// </para>
/// <para>
/// A third cannot be added quietly now: <c>OutboundCoverageTests</c> fails the
/// build on an outbound client that is not in a reviewed set. What that guard
/// cannot check is whether a listed client actually asks the door, which is what
/// the first test below is for.
/// </para>
/// </summary>
public sealed class BreachScreeningGovernanceTests
{
    private static readonly Uri RangeApi = new("https://api.pwnedpasswords.com/range/");

    /// <summary>
    /// A refused host means no call, and the password is allowed through.
    /// <para>
    /// <b>Failing open is the design and not an accident here.</b> A screening
    /// outage must not stop somebody recovering their account, and a host nobody
    /// allowed is an outage of exactly that kind — said in the log rather than
    /// silently.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ARefusedHostIsNotCalled()
    {
        var gateway = new RecordingGateway(allowed: false);

        bool breached = await Build(gateway).IsBreachedAsync("correct-horse-battery-staple");

        Assert.False(breached);
        Assert.True(gateway.WasAsked);
    }

    [Fact]
    public async Task ARefusedHostIsWrittenToTheCallLog()
    {
        var gateway = new RecordingGateway(allowed: false);

        await Build(gateway).IsBreachedAsync("correct-horse-battery-staple");

        OutboundAttempt attempt = Assert.Single(gateway.Attempts);

        Assert.Equal("breach-screening", attempt.Channel);
        Assert.False(attempt.Succeeded);
    }

    /// <summary>
    /// <b>Nothing about the password reaches the log.</b>
    /// <para>
    /// Not the password, not its hash, not the five-character prefix that would
    /// be sent. The call log is read by administrators and exported, and a row
    /// hinting that somebody's chosen password was found in a breach corpus is
    /// the last thing that should be browsable.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheCallLogLearnsNothingAboutThePassword()
    {
        var gateway = new RecordingGateway(allowed: false);

        await Build(gateway).IsBreachedAsync("correct-horse-battery-staple");

        OutboundAttempt attempt = Assert.Single(gateway.Attempts);

        string everything = $"{attempt.Channel} {attempt.Operation} {attempt.Destination} {attempt.Detail}";

        Assert.DoesNotContain("correct-horse", everything, StringComparison.OrdinalIgnoreCase);

        // The SHA-1 prefix of that password. Sending it is the whole point of
        // k-anonymity; storing it is not, because a prefix plus a timestamp
        // narrows the search for whoever reads the log.
        Assert.DoesNotContain("BE3DC", everything, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("api.pwnedpasswords.com", attempt.Destination);
    }

    /// <summary>
    /// Screening that is switched off asks nothing and records nothing.
    /// <para>
    /// There is no call to govern, and an allow-list refusal recorded against a
    /// feature nobody enabled would be a row that means something it does not.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ScreeningThatIsOffAsksNothing()
    {
        var gateway = new RecordingGateway(allowed: true);

        bool breached = await Build(gateway, enabled: false).IsBreachedAsync("anything");

        Assert.False(breached);
        Assert.False(gateway.WasAsked);
        Assert.Empty(gateway.Attempts);
    }

    // --- Fixtures -----------------------------------------------------------

    private static BreachedPasswordChecker Build(
        RecordingGateway gateway, bool enabled = true)
        => new(
            new HttpClient(),
            gateway,
            Options.Create(new BreachedPasswordOptions
            {
                UseExternalService = enabled,
                RangeApiBaseUrl = RangeApi,
                Timeout = TimeSpan.FromSeconds(1)
            }),
            new FixedClock(),
            NullLogger<BreachedPasswordChecker>.Instance);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
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
