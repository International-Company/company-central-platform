using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;

namespace CCP.Modules.Integrations.Domain.Logging;

/// <summary>
/// One call to an external provider, and what came back.
/// <para>
/// <b>What makes an integration dispute resolvable</b> (§19.4). "We never
/// received it" and "we sent it at 14:02 and you answered 200" are different
/// claims, and only one of them can be supported by a system that keeps no
/// record.
/// </para>
/// <para>
/// Append-only. There is no method here that edits a row, because a log somebody
/// can revise is not evidence.
/// </para>
/// <para>
/// It grows quickly, which is why it has a retention policy from the first day
/// rather than from the day somebody notices the table is the largest in the
/// database.
/// </para>
/// </summary>
public sealed class IntegrationCallLog : Entity
{
    private IntegrationCallLog() { }

    private IntegrationCallLog(
        Guid id,
        Guid providerId,
        string providerCode,
        string endpointKey,
        string method,
        string path,
        string correlationId,
        DateTimeOffset startedAt)
        : base(id)
    {
        ProviderId = providerId;
        ProviderCode = providerCode;
        EndpointKey = endpointKey;
        Method = method;
        Path = path;
        CorrelationId = correlationId;
        StartedAt = startedAt;
    }

    public Guid ProviderId { get; private set; }

    /// <summary>
    /// Denormalised on purpose. A provider that is later renamed or removed must
    /// not make its own history unreadable.
    /// </summary>
    public string ProviderCode { get; private set; } = string.Empty;

    public string EndpointKey { get; private set; } = string.Empty;

    public string Method { get; private set; } = string.Empty;

    /// <summary>
    /// The path as called, without the base address.
    /// <para>
    /// A query string is not stored: it is where credentials end up in systems
    /// that put them there, and a log is exactly where they must not.
    /// </para>
    /// </summary>
    public string Path { get; private set; } = string.Empty;

    /// <summary>
    /// Ties this call to the request that caused it, across the Platform's logs
    /// and the audit trail. The single most useful field during an incident.
    /// </summary>
    public string CorrelationId { get; private set; } = string.Empty;

    /// <summary>The request body, with declared sensitive fields already blanked.</summary>
    public string RequestPayload { get; private set; } = string.Empty;

    /// <summary>The response body, redacted the same way.</summary>
    public string ResponsePayload { get; private set; } = string.Empty;

    public int? StatusCode { get; private set; }

    public CallOutcome Outcome { get; private set; }

    /// <summary>
    /// What went wrong, when something did. Never the payload, and never a
    /// credential: this is the exception's message, which is the provider's or
    /// the runtime's words.
    /// </summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// How many attempts were made, including the first.
    /// <para>
    /// Recorded because "it worked" and "it worked on the fourth try" describe
    /// different providers, and only the second is a reason to go and look.
    /// </para>
    /// </summary>
    public int Attempts { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public int DurationMs { get; private set; }

    public static IntegrationCallLog Start(
        Guid providerId,
        string providerCode,
        string endpointKey,
        string method,
        string path,
        string correlationId,
        string redactedRequest,
        DateTimeOffset now)
        => new(Uuid7.NewGuid(now), providerId, providerCode, endpointKey, method, path,
               correlationId, now)
        {
            RequestPayload = redactedRequest,
            Outcome = CallOutcome.Pending
        };

    public void Complete(
        int statusCode,
        string redactedResponse,
        int attempts,
        DateTimeOffset now)
    {
        StatusCode = statusCode;
        ResponsePayload = redactedResponse;
        Attempts = attempts;
        CompletedAt = now;
        DurationMs = (int)(now - StartedAt).TotalMilliseconds;

        // The provider answered. A 500 from them is a completed call with a bad
        // status, not a failure of the Platform to reach them, and the two lead
        // an operator to different places.
        Outcome = statusCode is >= 200 and < 300 ? CallOutcome.Succeeded : CallOutcome.Refused;
    }

    public void Fail(CallOutcome outcome, string reason, int attempts, DateTimeOffset now)
    {
        Outcome = outcome;
        FailureReason = reason.Length > 1000 ? reason[..1000] : reason;
        Attempts = attempts;
        CompletedAt = now;
        DurationMs = (int)(now - StartedAt).TotalMilliseconds;
    }
}

/// <summary>How a call ended.</summary>
public enum CallOutcome
{
    /// <summary>Started and not yet finished. Rows left here are crashes.</summary>
    Pending = 0,

    /// <summary>The provider answered with a success status.</summary>
    Succeeded = 1,

    /// <summary>The provider answered, and said no.</summary>
    Refused = 2,

    /// <summary>No answer within the provider's timeout.</summary>
    TimedOut = 3,

    /// <summary>
    /// The circuit was open, so nothing was sent.
    /// <para>
    /// Logged rather than silent, because "we did not call them" is an answer to
    /// "why did nothing happen" and an absence of rows is not.
    /// </para>
    /// </summary>
    CircuitOpen = 4,

    /// <summary>The address was refused by the outbound policy.</summary>
    Blocked = 5,

    /// <summary>Something else went wrong on this side.</summary>
    Failed = 6
}
