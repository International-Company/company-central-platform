using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Application.Events;
using CCP.Kernel.Paging;
using CCP.Modules.Integrations.Domain.Logging;
using CCP.Modules.Integrations.Domain.Providers;
using CCP.Modules.Integrations.Domain.Webhooks;

namespace CCP.Modules.Integrations.Application.Abstractions;

/// <summary>Commits the module's changes as one transaction.</summary>
public interface IIntegrationUnitOfWork : IUnitOfWork;

/// <summary>The module's slice of the transactional outbox.</summary>
public interface IIntegrationOutbox : IOutbox;

/// <summary>
/// Turns the name of a secret into its value.
/// <para>
/// <b>The whole of credential-by-reference lives behind this interface</b>
/// (§19.3). The database holds names; this resolves them, at call time, from
/// wherever the deployment keeps secrets. A backup of the database therefore
/// contains no credential, and rotating one is an operation on the secret store
/// with no deployment and no downtime.
/// </para>
/// <para>
/// There is deliberately no method that <i>writes</i> a secret. Putting one here
/// would make this module a way to read them back — the Platform would then hold
/// every provider credential in a form it can print, which is exactly what
/// references exist to avoid.
/// </para>
/// </summary>
public interface ISecretResolver
{
    /// <summary>
    /// The value behind a reference, or null when there is none.
    /// <para>
    /// Null rather than an exception: a provider configured before its secret
    /// was created is an ordinary state during setup, and the caller says so
    /// plainly instead of the Platform failing in a way that looks like a bug.
    /// </para>
    /// </summary>
    Task<string?> ResolveAsync(string reference, CancellationToken cancellationToken = default);
}

/// <summary>
/// Decides whether the Platform may send a request to an address.
/// <para>
/// Implemented in the infrastructure layer because the honest answer needs a DNS
/// lookup, and the application layer does not do network calls.
/// </para>
/// </summary>
public interface IOutboundGuard
{
    /// <summary>
    /// Checks scheme, host, allow-list, and every address the host resolves to.
    /// <para>
    /// Asked before the credential is resolved, so a provider pointed at a host
    /// it was never allowed to reach cannot leak a secret to it. It is not the
    /// last word: see <see cref="ApproveAsync"/>.
    /// </para>
    /// </summary>
    Task<Domain.Outbound.OutboundHostPolicy.Verdict> InspectAsync(
        Uri destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a host and returns the addresses a connection may actually be
    /// made to.
    /// <para>
    /// <b>This is the last word, and it is spoken at the socket.</b>
    /// <see cref="InspectAsync"/> checks the address somebody asked for; this
    /// checks the address about to be dialled, and hands back the very addresses
    /// it checked so that nothing resolves the name a second time. Between two
    /// lookups, whoever controls the name decides what the second one says —
    /// which is DNS rebinding, and it turns an allowed host into a route to
    /// whatever the Platform can reach.
    /// </para>
    /// <para>
    /// It also catches the host nobody checked at all: a redirect sends the
    /// client somewhere the original URI never named, and only the connection
    /// layer sees where that is.
    /// </para>
    /// </summary>
    /// <param name="host">The host about to be connected to.</param>
    Task<Domain.Outbound.OutboundRoute> ApproveAsync(
        string host, CancellationToken cancellationToken = default);
}

/// <summary>
/// What a caller asks the integration layer to do.
/// </summary>
/// <param name="ProviderCode">Which registered provider.</param>
/// <param name="EndpointKey">Which of its operations.</param>
/// <param name="PathArguments">Values for the endpoint's path placeholders.</param>
/// <param name="Body">
/// The request body, already serialised. The layer transports it and does not
/// interpret it — a layer that understood the payload would be holding a
/// business rule.
/// </param>
/// <param name="Query">Query parameters, appended and escaped.</param>
/// <param name="Headers">
/// Extra headers. The credential is <b>not</b> supplied here: it is resolved
/// from the provider's reference, so a caller cannot attach one of its own and
/// cannot read the one that is used.
/// </param>
public sealed record IntegrationRequest(
    string ProviderCode,
    string EndpointKey,
    IReadOnlyDictionary<string, string>? PathArguments = null,
    string? Body = null,
    IReadOnlyDictionary<string, string>? Query = null,
    IReadOnlyDictionary<string, string>? Headers = null);

/// <summary>
/// What came back.
/// </summary>
/// <param name="StatusCode">The provider's status, or null when it never answered.</param>
/// <param name="Body">The response body, unredacted — the caller asked for it.</param>
/// <param name="Outcome">How it ended, as recorded in the call log.</param>
/// <param name="Attempts">How many tries it took.</param>
/// <param name="CallLogId">
/// The row recording this call. Handed back so a caller can quote it in its own
/// records and an operator can find the same call from either end.
/// </param>
public sealed record IntegrationResponse(
    int? StatusCode,
    string? Body,
    CallOutcome Outcome,
    int Attempts,
    Guid CallLogId)
{
    public bool Succeeded => Outcome == CallOutcome.Succeeded;
}

/// <summary>
/// The one door to the outside world (§19.1).
/// <para>
/// Every outbound call in the Platform goes through here, which is what makes
/// "what did we send them, and when?" answerable at all. A module that called an
/// external service directly would have its own retry behaviour, its own
/// credential handling and no entry in the log — and nobody would know until the
/// dispute.
/// </para>
/// </summary>
public interface IIntegrationConnector
{
    Task<IntegrationResponse> SendAsync(
        IntegrationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Reading and writing the module's own tables.</summary>
public interface IIntegrationRepository
{
    Task<IntegrationProvider?> FindProviderAsync(
        Guid providerId, CancellationToken cancellationToken = default);

    Task<IntegrationProvider?> FindProviderByCodeAsync(
        string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IntegrationProvider>> GetProvidersAsync(
        CancellationToken cancellationToken = default);

    void AddProvider(IntegrationProvider provider);

    void AddCallLog(IntegrationCallLog entry);

    Task<(IReadOnlyList<IntegrationCallLog> Items, long Total)> SearchCallLogAsync(
        string? providerCode,
        CallOutcome? outcome,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this exact signature has been accepted before.
    /// <para>
    /// The replay check. A correctly signed webhook that arrives twice is
    /// authentic both times — the signature proves who sent it and says nothing
    /// about whether it has already been acted on.
    /// </para>
    /// </summary>
    Task<bool> HasSeenWebhookAsync(
        string providerCode, string signature, CancellationToken cancellationToken = default);

    void AddWebhookReceipt(WebhookReceipt receipt);

    /// <summary>
    /// Removes call log rows and webhook receipts that are past their retention.
    /// Returns how many of each went.
    /// </summary>
    Task<(int Calls, int Receipts)> PurgeExpiredAsync(
        DateTimeOffset callsBefore,
        DateTimeOffset receiptsBefore,
        int batchSize,
        CancellationToken cancellationToken = default);
}
