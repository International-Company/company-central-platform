using System.Net;
using CCP.Modules.Integrations.Application;
using CCP.Modules.Integrations.Domain.Outbound;
using CCP.Modules.Integrations.Infrastructure.Outbound;
using Microsoft.Extensions.Logging.Abstractions;

namespace CCP.Modules.Integrations.UnitTests;

/// <summary>
/// The check made at the socket, where the destination stops being a question.
/// <para>
/// <b>This is the DNS rebinding defence.</b> Everything upstream works in names:
/// the allow-list is a list of names, the provider's base address is a name, and
/// the pre-flight check resolves that name and approves what it finds. If the
/// HTTP client then resolves the name a second time to connect, whoever runs
/// that name's DNS chooses the second answer — so the approval covers one
/// address and the connection goes to another, which is how a host somebody
/// deliberately allowed becomes a route to the cloud metadata service.
/// </para>
/// <para>
/// The fix is not a better check; it is one lookup instead of two. So what these
/// assert is mostly that the approved addresses <b>come back</b>, because that
/// is the whole mechanism.
/// </para>
/// </summary>
public sealed class OutboundApprovalTests
{
    /// <summary>
    /// A literal public address on the list is approved as itself. There is
    /// nothing to resolve, so there is nothing to rebind.
    /// </summary>
    [Fact]
    public async Task AnAllowedLiteralIsApprovedAsItself()
    {
        OutboundRoute route = await Guard("203.0.113.10").ApproveAsync("203.0.113.10");

        Assert.True(route.IsAllowed);
        Assert.Equal([IPAddress.Parse("203.0.113.10")], route.Addresses);
    }

    /// <summary>
    /// <c>169.254.169.254</c> is the cloud metadata service, and it hands the
    /// instance's credentials to anything that asks. Being on somebody's
    /// allow-list does not make it reachable.
    /// </summary>
    [Theory]
    [InlineData("169.254.169.254")]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("::1")]
    public async Task APrivateLiteralIsRefusedEvenWhenItIsOnTheList(string host)
    {
        OutboundRoute route = await Guard(host).ApproveAsync(host);

        Assert.Equal(OutboundHostPolicy.Verdict.PrivateAddressLiteral, route.Verdict);
        Assert.Empty(route.Addresses);
    }

    /// <summary>
    /// The host arriving at the socket is not always the one anybody checked: a
    /// redirect changes it, and the connection layer is the only place that sees
    /// where to. So the list is consulted again here.
    /// </summary>
    [Fact]
    public async Task AHostNobodyAllowedIsRefused()
    {
        OutboundRoute route = await Guard("allowed.example.com").ApproveAsync("203.0.113.10");

        Assert.Equal(OutboundHostPolicy.Verdict.HostNotAllowed, route.Verdict);
        Assert.Empty(route.Addresses);
    }

    /// <summary>
    /// A name that does not resolve is refused rather than allowed. A resolver
    /// failure must not be a way past the check.
    /// </summary>
    [Fact]
    public async Task ANameThatDoesNotResolveIsRefused()
    {
        // .invalid is reserved by RFC 2606 precisely so that it never resolves.
        OutboundRoute route = await Guard(".invalid").ApproveAsync("nothing.invalid");

        Assert.Equal(OutboundHostPolicy.Verdict.ResolvesToPrivateAddress, route.Verdict);
        Assert.Empty(route.Addresses);
    }

    /// <summary>
    /// A name resolving to loopback is refused, which is the rebinding payload
    /// itself: an attacker's name, on the list or not, pointed at the machine
    /// the Platform runs on.
    /// </summary>
    [Fact]
    public async Task ANameResolvingInsideTheNetworkIsRefused()
    {
        // localhost resolves to loopback on every machine this will ever run on,
        // which makes it the one name a test can rely on for this.
        OutboundRoute route = await Guard("localhost").ApproveAsync("localhost");

        Assert.Equal(OutboundHostPolicy.Verdict.ResolvesToPrivateAddress, route.Verdict);
        Assert.Empty(route.Addresses);
    }

    /// <summary>
    /// A refusal never carries addresses, so a caller that ignored the verdict
    /// would still have nothing to dial.
    /// </summary>
    [Fact]
    public async Task ARefusalLeavesNothingToConnectTo()
    {
        foreach (string host in new[] { "10.0.0.5", "not-listed.example.com", "localhost" })
        {
            OutboundRoute route = await Guard("something-else.example.com").ApproveAsync(host);

            Assert.False(route.IsAllowed);
            Assert.Empty(route.Addresses);
        }
    }

    /// <summary>
    /// An empty allow-list means no outbound calls at all — the safe direction
    /// to fail in, and the same answer here as at the pre-flight check.
    /// </summary>
    [Fact]
    public async Task AnEmptyAllowListApprovesNothing()
    {
        OutboundRoute route = await Guard().ApproveAsync("203.0.113.10");

        Assert.False(route.IsAllowed);
    }

    private static OutboundGuard Guard(params string[] allowedHosts)
        => new(
            new IntegrationOptions { AllowedHosts = [.. allowedHosts] },
            NullLogger<OutboundGuard>.Instance);
}
