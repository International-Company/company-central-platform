using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Application.Events;

namespace CCP.Modules.Identity.Application.Abstractions;

/// <summary>
/// The Identity module's unit of work.
/// <para>
/// A module-specific interface rather than the kernel's <see cref="IUnitOfWork"/>
/// directly, because the container resolves by type: if every module registered
/// its own implementation of the shared interface, the last registration would
/// win and the other modules would silently commit through the wrong context.
/// One interface per module makes that impossible.
/// </para>
/// <para>
/// Committing through this saves the module's entities and any events staged on
/// <see cref="IIdentityOutbox"/> in a single transaction — the guarantee the
/// outbox pattern exists to provide (ARCHITECTURE.md §8.5).
/// </para>
/// </summary>
public interface IIdentityUnitOfWork : IUnitOfWork;

/// <summary>
/// The Identity module's outbox.
/// <para>
/// Module-specific for the same reason as <see cref="IIdentityUnitOfWork"/>, and
/// bound to the same <c>DbContext</c>, so a staged event and the change that
/// produced it commit or roll back together.
/// </para>
/// </summary>
public interface IIdentityOutbox : IOutbox;
