using System.Net;
using System.Net.Sockets;
using CCP.Modules.Integrations.Application;
using CCP.Modules.Integrations.Application.Abstractions;
using CCP.Modules.Integrations.Domain.Outbound;
using Microsoft.Extensions.Logging;

namespace CCP.Modules.Integrations.Infrastructure.Outbound;

/// <summary>
/// Applies the outbound policy, including the half that needs DNS.
/// <para>
/// Two checks, and both must pass: the <b>name</b> must be on the allow-list, and
/// every <b>address it resolves to</b> must be public. The second exists because
/// a name on the list today can be pointed at <c>127.0.0.1</c> tomorrow by
/// whoever controls its DNS — and the allow-list alone would happily let the
/// Platform call itself, or the cloud metadata service that hands out the
/// instance's credentials to anything that asks.
/// </para>
/// <para>
/// <b>The known gap, stated rather than hidden.</b> Between this check and the
/// connection, the name can be re-resolved to a different address — DNS
/// rebinding. Closing it means connecting to a checked address rather than to a
/// name, which requires taking over socket connection in the HTTP handler.
/// Recorded as debt; the allow-list means an attacker would first need control
/// of a host somebody deliberately allowed, which is a much narrower position
/// than the general case this defends against.
/// </para>
/// </summary>
public sealed class OutboundGuard(
    IntegrationOptions options,
    ILogger<OutboundGuard> logger) : IOutboundGuard
{
    public async Task<OutboundHostPolicy.Verdict> InspectAsync(
        Uri destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        OutboundHostPolicy.Verdict verdict =
            OutboundHostPolicy.Inspect(destination, options.AllowedHosts);

        if (verdict != OutboundHostPolicy.Verdict.Allowed)
        {
            Refused(destination, verdict);

            return verdict;
        }

        // A literal address has already been checked against the private ranges
        // by Inspect; there is nothing to resolve.
        if (IPAddress.TryParse(destination.Host, out _))
        {
            return OutboundHostPolicy.Verdict.Allowed;
        }

        IPAddress[] addresses;

        try
        {
            addresses = await Dns.GetHostAddressesAsync(destination.Host, cancellationToken);
        }
        catch (SocketException)
        {
            // The name does not resolve. Refused rather than allowed: a resolver
            // failure must not be a way to skip the check.
            Refused(destination, OutboundHostPolicy.Verdict.ResolvesToPrivateAddress);

            return OutboundHostPolicy.Verdict.ResolvesToPrivateAddress;
        }

        verdict = OutboundHostPolicy.InspectAddresses(addresses);

        if (verdict != OutboundHostPolicy.Verdict.Allowed)
        {
            Refused(destination, verdict);
        }

        return verdict;
    }

    /// <summary>
    /// Records exactly which check failed.
    /// <para>
    /// Here and not in the response. The caller is told only that the address
    /// was refused — learning <i>why</i> would let it map the internal network
    /// one probe at a time — while an operator reading this log can see
    /// immediately whether somebody forgot an allow-list entry or somebody is
    /// trying to reach the metadata service.
    /// </para>
    /// </summary>
    private void Refused(Uri destination, OutboundHostPolicy.Verdict verdict)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning(
                "Outbound call to {Host} refused: {Verdict}.", destination.Host, verdict);
        }
    }
}
