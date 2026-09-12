using System.Net;
using System.Net.Sockets;

namespace CCP.Modules.Integrations.Domain.Outbound;

/// <summary>
/// Whether the Platform may send a request to a given address.
/// <para>
/// <b>This is the SSRF defence, and it is deny-by-default.</b> A host that
/// nobody has explicitly allowed is refused, which means an empty allow-list is
/// a Platform that makes no outbound calls at all — the safe direction to fail
/// in, and one an operator notices immediately.
/// </para>
/// <para>
/// The attack it prevents is worth stating plainly, because it does not look
/// dangerous until it happens. A layer that will fetch a URL somebody else chose
/// is a proxy into the network it runs in: the cloud metadata service at
/// <c>169.254.169.254</c> hands out the instance's credentials to anything that
/// asks, internal admin interfaces are usually unauthenticated because "they are
/// not reachable from outside", and the Platform is inside. Every one of those is
/// reachable from a request that says "call this URL for me".
/// </para>
/// <para>
/// So two independent checks, and both must pass. The <b>name</b> must be on the
/// list, and every <b>address it resolves to</b> must be a public one — because a
/// name on the list today can be pointed at <c>127.0.0.1</c> tomorrow by whoever
/// controls its DNS, and the allow-list alone would not notice.
/// </para>
/// <para>
/// And the checked addresses are what gets connected to. Checking a name and
/// then dialling the name resolves twice, and whoever controls the name decides
/// what the second answer is; see <see cref="OutboundRoute"/>.
/// </para>
/// </summary>
public static class OutboundHostPolicy
{
    /// <summary>Why an address was refused, in a form a log can carry.</summary>
    public enum Verdict
    {
        /// <summary>The request may be sent.</summary>
        Allowed = 0,

        /// <summary>Not HTTP or HTTPS.</summary>
        SchemeNotAllowed = 1,

        /// <summary>The host is not on the allow-list.</summary>
        HostNotAllowed = 2,

        /// <summary>The host resolves to an address inside the network.</summary>
        ResolvesToPrivateAddress = 3,

        /// <summary>The host is a literal address inside the network.</summary>
        PrivateAddressLiteral = 4,

        /// <summary>Credentials in the URL, which is a redirect trick.</summary>
        CredentialsInUri = 5
    }

    /// <summary>
    /// Checks everything that can be decided without a network lookup.
    /// <para>
    /// Separate from the resolution check so that the cheap refusals happen
    /// first and so this half can be tested without DNS — which matters,
    /// because a security check that only runs when the network cooperates is
    /// one nobody tests.
    /// </para>
    /// </summary>
    /// <param name="uri">Where the request would go.</param>
    /// <param name="allowedHosts">
    /// Host names that have been explicitly allowed. Matched exactly, or as a
    /// subdomain of an entry written <c>.example.com</c>.
    /// </param>
    public static Verdict Inspect(Uri uri, IReadOnlyCollection<string> allowedHosts)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(allowedHosts);

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            // file://, gopher://, ftp:// and the rest. Each has been a way to
            // read local files or reach services that never expected a request.
            return Verdict.SchemeNotAllowed;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            // https://allowed.example.com@evil.test/ points at evil.test and
            // reads, to a human skimming a log, as the allowed host.
            return Verdict.CredentialsInUri;
        }

        // A literal address is checked before the name list, because an entry
        // like "10.0.0.5" on somebody's allow-list should not be honoured.
        if (IPAddress.TryParse(uri.Host, out IPAddress? literal))
        {
            return IsPublic(literal) && IsAllowedHost(uri.Host, allowedHosts)
                ? Verdict.Allowed
                : IsPublic(literal) ? Verdict.HostNotAllowed : Verdict.PrivateAddressLiteral;
        }

        return IsAllowedHost(uri.Host, allowedHosts) ? Verdict.Allowed : Verdict.HostNotAllowed;
    }

    /// <summary>
    /// The second half: every address the name resolves to must be public.
    /// <para>
    /// <b>Every</b> address, not the first. A name that resolves to one public
    /// address and one private one is a name being used to reach the private
    /// one, and checking only the first is a coin flip.
    /// </para>
    /// <para>
    /// The addresses this passed are the ones the connection is made to, so
    /// there is no second lookup between the check and the socket. See
    /// <see cref="OutboundRoute"/>.
    /// </para>
    /// </summary>
    public static Verdict InspectAddresses(IReadOnlyCollection<IPAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);

        if (addresses.Count == 0)
        {
            // A name that resolves to nothing cannot be reached anyway, and
            // treating it as allowed would mean a resolver failure quietly
            // skipping the check.
            return Verdict.ResolvesToPrivateAddress;
        }

        return addresses.All(IsPublic) ? Verdict.Allowed : Verdict.ResolvesToPrivateAddress;
    }

    /// <summary>
    /// Whether a host name is on the list.
    /// <para>
    /// Exact match, or a subdomain of an entry beginning with a dot. Suffix
    /// matching without the dot is the classic mistake: an entry of
    /// <c>example.com</c> matching <c>notexample.com</c> is an allow-list that
    /// allows a host somebody else registered.
    /// </para>
    /// <para>
    /// Public because the connection layer asks it again, on the host it is
    /// actually about to dial. A redirect changes the host without changing the
    /// request that was checked, so the last word has to be spoken at the socket
    /// rather than at the URI somebody started from.
    /// </para>
    /// </summary>
    public static bool IsAllowedHost(string host, IReadOnlyCollection<string> allowedHosts)
    {
        foreach (string allowed in allowedHosts)
        {
            string entry = allowed.Trim();

            if (entry.Length == 0)
            {
                continue;
            }

            if (string.Equals(host, entry, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (entry.StartsWith('.')
                && host.EndsWith(entry, StringComparison.OrdinalIgnoreCase)
                && host.Length > entry.Length)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether an address is out on the internet rather than inside something.
    /// <para>
    /// The list is long because the ways in are many, and every one of them has
    /// been used: loopback reaches the Platform's own admin surface, link-local
    /// reaches the cloud metadata service that hands out instance credentials,
    /// and the private ranges reach whatever else the company runs in the same
    /// network.
    /// </para>
    /// </summary>
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // An IPv4 address wearing an IPv6 coat. ::ffff:127.0.0.1 is
            // loopback, and a check that looked only at the v6 form would let it
            // through.
            if (address.IsIPv4MappedToIPv6)
            {
                return IsPublic(address.MapToIPv4());
            }

            if (IPAddress.IsLoopback(address)
                || address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6UniqueLocal
                || address.IsIPv6Multicast
                || address.Equals(IPAddress.IPv6Any))
            {
                return false;
            }

            return true;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        byte[] octets = address.GetAddressBytes();

        return octets[0] switch
        {
            0 => false,                                   // 0.0.0.0/8, "this network"
            10 => false,                                  // private
            127 => false,                                 // loopback
            100 when octets[1] is >= 64 and <= 127 => false,   // carrier-grade NAT
            169 when octets[1] == 254 => false,           // link-local, and the metadata service
            172 when octets[1] is >= 16 and <= 31 => false,    // private
            192 when octets[1] == 168 => false,           // private
            192 when octets[1] == 0 && octets[2] == 0 => false,  // IETF protocol assignments
            198 when octets[1] is 18 or 19 => false,      // benchmarking
            >= 224 => false,                              // multicast and reserved
            _ => true
        };
    }
}
