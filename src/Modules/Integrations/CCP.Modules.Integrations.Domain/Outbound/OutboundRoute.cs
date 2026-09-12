using System.Net;

namespace CCP.Modules.Integrations.Domain.Outbound;

/// <summary>
/// Where a connection is permitted to go: the verdict, and the exact addresses
/// it was reached on.
/// <para>
/// <b>The addresses are the point.</b> Checking a name and then connecting to
/// the name means resolving twice, and whoever controls the name decides what
/// the second answer is — which is DNS rebinding, and it turns a host somebody
/// allowed into a way to reach the machine the Platform runs on. Carrying the
/// checked addresses forward and connecting to one of <i>those</i> removes the
/// second lookup, and with it the window.
/// </para>
/// <para>
/// Empty whenever the verdict is anything but <see cref="OutboundHostPolicy.Verdict.Allowed"/>,
/// so a caller that ignores the verdict still has nothing to connect to.
/// </para>
/// </summary>
/// <param name="Verdict">Whether the connection may be made, and if not, why.</param>
/// <param name="Addresses">The addresses that passed the check.</param>
public sealed record OutboundRoute(
    OutboundHostPolicy.Verdict Verdict,
    IReadOnlyList<IPAddress> Addresses)
{
    public bool IsAllowed => Verdict == OutboundHostPolicy.Verdict.Allowed;

    public static OutboundRoute Refused(OutboundHostPolicy.Verdict verdict)
        => new(verdict, []);

    public static OutboundRoute Allowed(IReadOnlyList<IPAddress> addresses)
        => new(OutboundHostPolicy.Verdict.Allowed, addresses);
}
