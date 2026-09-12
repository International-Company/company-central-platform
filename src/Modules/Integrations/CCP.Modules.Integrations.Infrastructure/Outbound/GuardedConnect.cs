using System.Net;
using System.Net.Sockets;
using CCP.Modules.Integrations.Application.Abstractions;
using CCP.Modules.Integrations.Domain.Outbound;
using Microsoft.Extensions.Logging;

namespace CCP.Modules.Integrations.Infrastructure.Outbound;

/// <summary>
/// Opens every outbound socket, and refuses the ones the policy does not permit.
/// <para>
/// <b>The check has to happen here, because here is where the address is
/// finally decided.</b> Everything above works in names: the allow-list is a
/// list of names, the provider's base address is a name, and the check before a
/// call resolves that name and approves what it finds. Then the HTTP client
/// resolves it again to connect — and between those two lookups, whoever
/// controls the name chooses the second answer. That is DNS rebinding, and it
/// turns a host somebody deliberately allowed into a route to the cloud
/// metadata service, or to the Platform's own admin surface.
/// </para>
/// <para>
/// So this resolves once and connects to what it resolved. There is no second
/// lookup to poison.
/// </para>
/// <para>
/// It also catches the destination nobody upstream ever saw. A redirect sends
/// the client to a host the original URI never named, and the connection layer
/// is the only place that learns where.
/// </para>
/// <para>
/// <b>A refusal is not a network error.</b> It is thrown as
/// <see cref="OutboundRefusedException"/> so the call is logged as blocked
/// rather than failed, and so the resilience pipeline neither retries it nor
/// counts it towards opening the provider's circuit — a policy decision does not
/// become truer on the third attempt, and a provider that is perfectly healthy
/// should not be marked down because somebody pointed it somewhere it may not
/// go.
/// </para>
/// </summary>
public sealed class GuardedConnect(IOutboundGuard guard, ILogger<GuardedConnect> logger)
{
    /// <summary>
    /// How long a pooled connection may be reused.
    /// <para>
    /// The default is forever, which would mean a connection approved once is
    /// kept regardless of what the name resolves to afterwards. Two minutes is
    /// short enough that the guard is consulted again on any long-lived
    /// integration and long enough that an ordinary burst of calls still shares
    /// one connection.
    /// </para>
    /// </summary>
    public static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(2);

    public async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string host = context.DnsEndPoint.Host;
        int port = context.DnsEndPoint.Port;

        OutboundRoute route = await guard.ApproveAsync(host, cancellationToken);

        if (!route.IsAllowed || route.Addresses.Count == 0)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    "Outbound connection to {Host}:{Port} refused at the socket: {Verdict}.",
                    host, port, route.Verdict);
            }

            throw new OutboundRefusedException(host, route.Verdict);
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
        {
            // What SocketsHttpHandler sets on the connections it makes itself.
            // Taking over the callback means taking over the settings that came
            // with it.
            NoDelay = true
        };

        try
        {
            // The addresses, not the name. This is the whole point of the class:
            // the host name is never handed to a resolver again.
            await socket.ConnectAsync(
                [.. route.Addresses], port, cancellationToken);

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();

            throw;
        }
    }
}

/// <summary>
/// The policy refused a connection.
/// <para>
/// A distinct type because the difference matters twice: the call log should say
/// <c>Blocked</c> rather than <c>Failed</c>, and the resilience pipeline should
/// not retry it or hold it against the provider.
/// </para>
/// </summary>
public sealed class OutboundRefusedException : Exception
{
    public OutboundRefusedException(string host, OutboundHostPolicy.Verdict verdict)
        : base($"An outbound connection to '{host}' was refused: {verdict}.")
    {
        Host = host;
        Verdict = verdict;
    }

    public OutboundRefusedException()
        : this(string.Empty, OutboundHostPolicy.Verdict.HostNotAllowed)
    {
    }

    public OutboundRefusedException(string message)
        : base(message)
    {
        Host = string.Empty;
        Verdict = OutboundHostPolicy.Verdict.HostNotAllowed;
    }

    public OutboundRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
        Host = string.Empty;
        Verdict = OutboundHostPolicy.Verdict.HostNotAllowed;
    }

    public string Host { get; } = string.Empty;

    public OutboundHostPolicy.Verdict Verdict { get; }

    /// <summary>
    /// Finds a refusal inside whatever the HTTP stack wrapped it in.
    /// <para>
    /// <c>HttpClient</c> presents anything a connect callback threw as an
    /// <c>HttpRequestException</c>, and a resilience pipeline may wrap that
    /// again. Walking the chain is what keeps the distinction from being lost
    /// the moment it leaves this class.
    /// </para>
    /// </summary>
    public static OutboundRefusedException? Find(Exception? exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is OutboundRefusedException refused)
            {
                return refused;
            }
        }

        return null;
    }
}
