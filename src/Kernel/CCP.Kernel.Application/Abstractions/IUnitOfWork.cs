namespace CCP.Kernel.Application.Abstractions;

/// <summary>
/// Commits a module's changes as one atomic unit, together with any integration
/// events produced.
/// <para>
/// The outbox write happens inside this same transaction. That is what
/// guarantees an event is never recorded for a change that rolled back, and
/// never lost after a change that committed (ARCHITECTURE.md §8.5).
/// </para>
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
