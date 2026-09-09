using System.Net;
using CCP.Modules.Integrations.Domain.Outbound;

namespace CCP.Modules.Integrations.UnitTests;

/// <summary>
/// Where the Platform may and may not send a request.
/// <para>
/// <b>The most important tests in this module.</b> A layer that will fetch a URL
/// somebody else chose is a proxy into the network it runs in — the cloud
/// metadata service hands out the instance's credentials to anything that asks,
/// and internal admin interfaces are usually unauthenticated because "they are
/// not reachable from outside". The Platform is inside.
/// </para>
/// </summary>
public sealed class OutboundHostPolicyTests
{
    private static readonly string[] Allowed = ["api.example.com", ".partner.test"];

    private static OutboundHostPolicy.Verdict Inspect(string url) =>
        OutboundHostPolicy.Inspect(new Uri(url), Allowed);

    [Fact]
    public void AnAllowedHostIsAllowed()
    {
        Assert.Equal(
            OutboundHostPolicy.Verdict.Allowed,
            Inspect("https://api.example.com/transfer"));
    }

    [Fact]
    public void AHostNobodyAllowedIsRefused()
    {
        // Deny by default. An empty or incomplete list means fewer calls, not
        // more — the safe direction, and one an operator notices immediately.
        Assert.Equal(
            OutboundHostPolicy.Verdict.HostNotAllowed,
            Inspect("https://somewhere-else.test/"));
    }

    [Fact]
    public void ASubdomainMatchesOnlyWhenTheEntryBeginsWithADot()
    {
        Assert.Equal(
            OutboundHostPolicy.Verdict.Allowed,
            Inspect("https://api.partner.test/hook"));

        // "api.example.com" is exact, so a subdomain of it is not covered.
        Assert.Equal(
            OutboundHostPolicy.Verdict.HostNotAllowed,
            Inspect("https://evil.api.example.com/"));
    }

    [Fact]
    public void SuffixMatchingDoesNotLeakToADifferentRegistration()
    {
        // The classic mistake: an entry of "partner.test" matching
        // "notpartner.test", which somebody else can register.
        Assert.Equal(
            OutboundHostPolicy.Verdict.HostNotAllowed,
            Inspect("https://notpartner.test/"));
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://api.example.com/")]
    [InlineData("gopher://api.example.com/")]
    public void OnlyHttpAndHttpsAreAllowed(string url)
    {
        Assert.Equal(
            OutboundHostPolicy.Verdict.SchemeNotAllowed,
            OutboundHostPolicy.Inspect(new Uri(url), Allowed));
    }

    [Fact]
    public void CredentialsInTheUrlAreRefused()
    {
        // https://api.example.com@evil.test/ points at evil.test and reads, to
        // anybody skimming a log, as the allowed host.
        Assert.Equal(
            OutboundHostPolicy.Verdict.CredentialsInUri,
            Inspect("https://api.example.com@evil.test/"));
    }

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://localhost.example/")]     // not on the list either way
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://172.16.4.1/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://[::1]/")]
    public void AddressesInsideTheNetworkAreRefused(string url)
    {
        Assert.NotEqual(OutboundHostPolicy.Verdict.Allowed, Inspect(url));
    }

    [Fact]
    public void TheMetadataServiceIsRefusedEvenIfSomebodyAllowsIt()
    {
        // A literal address is checked against the private ranges before the
        // name list, so an entry of "169.254.169.254" on somebody's allow-list
        // does not honour it. That address hands out the instance's credentials
        // to anything that asks.
        Assert.Equal(
            OutboundHostPolicy.Verdict.PrivateAddressLiteral,
            OutboundHostPolicy.Inspect(
                new Uri("http://169.254.169.254/latest/meta-data/"),
                ["169.254.169.254"]));
    }

    [Fact]
    public void AnIPv4AddressWearingAnIPv6CoatIsStillLoopback()
    {
        // ::ffff:127.0.0.1. A check that looked only at the v6 form would let it
        // straight through.
        Assert.False(OutboundHostPolicy.IsPublic(IPAddress.Parse("::ffff:127.0.0.1")));
        Assert.False(OutboundHostPolicy.IsPublic(IPAddress.Parse("::ffff:10.0.0.1")));
    }

    [Fact]
    public void EveryResolvedAddressMustBePublic()
    {
        // A name resolving to one public address and one private one is a name
        // being used to reach the private one. Checking only the first is a coin
        // flip.
        Assert.Equal(
            OutboundHostPolicy.Verdict.ResolvesToPrivateAddress,
            OutboundHostPolicy.InspectAddresses(
            [
                IPAddress.Parse("93.184.216.34"),
                IPAddress.Parse("127.0.0.1")
            ]));

        Assert.Equal(
            OutboundHostPolicy.Verdict.Allowed,
            OutboundHostPolicy.InspectAddresses([IPAddress.Parse("93.184.216.34")]));
    }

    [Fact]
    public void ANameThatResolvesToNothingIsRefused()
    {
        // A resolver failure must not be a way to skip the check.
        Assert.Equal(
            OutboundHostPolicy.Verdict.ResolvesToPrivateAddress,
            OutboundHostPolicy.InspectAddresses([]));
    }

    [Fact]
    public void PublicAddressesArePublic()
    {
        Assert.True(OutboundHostPolicy.IsPublic(IPAddress.Parse("8.8.8.8")));
        Assert.True(OutboundHostPolicy.IsPublic(IPAddress.Parse("93.184.216.34")));
        Assert.True(OutboundHostPolicy.IsPublic(IPAddress.Parse("2606:2800:220:1:248:1893:25c8:1946")));
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("100.64.0.1")]      // carrier-grade NAT
    [InlineData("198.18.0.1")]      // benchmarking
    [InlineData("224.0.0.1")]       // multicast
    public void TheLessObviousRangesAreAlsoRefused(string address)
    {
        Assert.False(OutboundHostPolicy.IsPublic(IPAddress.Parse(address)));
    }
}
