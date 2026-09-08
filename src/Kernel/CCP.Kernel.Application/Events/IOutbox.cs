using CCP.Kernel.Domain;

namespace CCP.Kernel.Application.Events;

/// <summary>
/// Stages an integration event for reliable delivery.
/// <para>
/// The event is written to the outbox table in the caller's transaction and is
/// dispatched later by a background relay. Callers must not dispatch events
/// directly: an event sent before the commit may describe a change that never
/// happened, and one sent after the commit may be lost if the process dies in
/// between.
/// </para>
/// </summary>
public interface IOutbox
{
    /// <summary>
    /// Enqueues an event within the current transaction. It becomes visible to
    /// the relay only when that transaction commits.
    /// </summary>
    Task EnqueueAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}
