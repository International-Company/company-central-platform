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
/// <b>Neither check is the last word, and that is why <see cref="ApproveAsync"/>
/// exists.</b> This one runs against the address somebody asked for, before the
/// credential is resolved, so a provider pointed somewhere it may not go cannot
/// leak a secret on the way to being refused. But between a check and a
/// connection the name can be resolved again to something else — DNS rebinding —
/// and a redirect can send the client to a host this method never saw at all.
/// Both are settled at the socket, where the address stops being a question.
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
    /// The check at the socket: resolve once, approve, and hand back the very
    /// addresses that were approved.
    /// <para>
    /// Returning the addresses rather than a yes is the substance of it. A
    /// caller given permission to dial a <i>name</i> has to resolve it again,
    /// and the answer to that second lookup is chosen by whoever runs the name's
    /// DNS — so the approval would cover one address and the connection would go
    /// to another. There is no second lookup here.
    /// </para>
    /// <para>
    /// The host is checked against the allow-list again as well, because the
    /// host arriving here is not always the one anybody checked: a redirect
    /// changes it, and only the connection layer sees where to.
    /// </para>
    /// </summary>
    public async Task<OutboundRoute> ApproveAsync(
        string host, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        if (IPAddress.TryParse(host, out IPAddress? literal))
        {
            if (!OutboundHostPolicy.IsPublic(literal))
            {
                return OutboundRoute.Refused(OutboundHostPolicy.Verdict.PrivateAddressLiteral);
            }

            return OutboundHostPolicy.IsAllowedHost(host, options.AllowedHosts)
                ? OutboundRoute.Allowed([literal])
                : OutboundRoute.Refused(OutboundHostPolicy.Verdict.HostNotAllowed);
        }

        if (!OutboundHostPolicy.IsAllowedHost(host, options.AllowedHosts))
        {
            return OutboundRoute.Refused(OutboundHostPolicy.Verdict.HostNotAllowed);
        }

        IPAddress[] addresses;

        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        }
        catch (SocketException)
        {
            // Refused, not allowed. A resolver failure must not be a way past
            // the check, and a name that resolves to nothing cannot be reached
            // anyway.
            return OutboundRoute.Refused(OutboundHostPolicy.Verdict.ResolvesToPrivateAddress);
        }

        OutboundHostPolicy.Verdict verdict = OutboundHostPolicy.InspectAddresses(addresses);

        return verdict == OutboundHostPolicy.Verdict.Allowed
            ? OutboundRoute.Allowed(addresses)
            : OutboundRoute.Refused(verdict);
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
