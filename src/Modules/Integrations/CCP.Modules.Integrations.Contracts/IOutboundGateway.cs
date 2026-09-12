namespace CCP.Modules.Integrations.Contracts;

/// <summary>
/// The governed door, for the protocols that are not HTTP.
/// <para>
/// <b>Every outbound call passes one door</b> (§19.1) — except that until now,
/// one did not. The email channel opened a socket to a mail server directly:
/// no allow-list, no entry in the call log, and no way for an operator to
/// discover that the Platform had been failing to send anything for a day.
/// It was recorded twice, as #33 and #45, and the reason it stayed open is
/// stated in both: <b>SMTP is not HTTP</b>, so the connector cannot carry it.
/// </para>
/// <para>
/// What <i>can</i> be carried is the part that was never about HTTP. An
/// allow-list is a question about a host name. A call log is a row. Neither
/// cares which protocol follows, and this is the narrow surface that offers them
/// to a channel that speaks something else.
/// </para>
/// <para>
/// Deliberately not a way to send anything. A module that wanted the Platform to
/// make an HTTP call on its behalf uses the connector, where the provider, its
/// credential and its resilience settings are configuration somebody can see.
/// </para>
/// </summary>
public interface IOutboundGateway
{
    /// <summary>
    /// Whether the Platform may open a connection to this host.
    /// <para>
    /// The same allow-list and the same private-address checks every other
    /// outbound call passes, asked before the socket rather than after it. A
    /// mail host that nobody allowed is a mail host the Platform will not reach,
    /// and finding that out at configuration time is better than finding it out
    /// from a queue that stopped draining.
    /// </para>
    /// </summary>
    Task<OutboundApproval> ApproveHostAsync(
        string host, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records one attempt in the shared call log.
    /// <para>
    /// So that "what has the Platform been sending, and did it arrive" has one
    /// answer covering every channel, rather than one answer for HTTP providers
    /// and a shrug for everything else.
    /// </para>
    /// </summary>
    Task RecordAttemptAsync(
        OutboundAttempt attempt, CancellationToken cancellationToken = default);
}

/// <summary>
/// Whether a host may be reached, and why not.
/// </summary>
/// <param name="IsAllowed">Whether to open the connection.</param>
/// <param name="Reason">
/// What the policy said, for the log. <b>Not for the caller's caller</b> — the
/// reason a specific address was refused maps the internal network one probe at
/// a time.
/// </param>
public sealed record OutboundApproval(bool IsAllowed, string Reason);

/// <summary>
/// One outbound attempt that was not an HTTP provider call.
/// </summary>
/// <param name="Channel">
/// What the log calls it. A stable short name — <c>smtp</c> — because it becomes
/// a provider code in the call log and in the health view beside it.
/// </param>
/// <param name="Operation">Which kind of attempt, e.g. <c>send</c>.</param>
/// <param name="Destination">
/// Where it went, in a form safe to store. A host name, never a credential and
/// never a recipient's address: the call log is read by administrators and
/// exported, and a list of who was emailed is not theirs to browse.
/// </param>
/// <param name="Succeeded">Whether the other end accepted it.</param>
/// <param name="Detail">What went wrong, when something did.</param>
/// <param name="DurationMs">How long it took.</param>
public sealed record OutboundAttempt(
    string Channel,
    string Operation,
    string Destination,
    bool Succeeded,
    string? Detail,
    double DurationMs);
