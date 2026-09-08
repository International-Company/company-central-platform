namespace CCP.Kernel.Application.Abstractions;

/// <summary>
/// Identifiers that tie one request together across logs, traces and audit
/// records (ARCHITECTURE.md §22.1).
/// </summary>
public interface IRequestContext
{
    /// <summary>Identifies this single HTTP request.</summary>
    string RequestId { get; }

    /// <summary>
    /// Identifies the whole operation, potentially spanning several systems.
    /// Accepted from the caller when supplied, generated when not — so a
    /// business application's call and the Platform's handling of it share one
    /// identifier.
    /// </summary>
    string CorrelationId { get; }

    /// <summary>Caller IP address, when it can be determined.</summary>
    string? IpAddress { get; }

    /// <summary>Caller user agent, when supplied.</summary>
    string? UserAgent { get; }
}
