using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Kernel.Application.Modules;

/// <summary>
/// A capability module's single registration point (ARCHITECTURE.md §7.1).
/// <para>
/// Modules are registered explicitly in a list in the host rather than
/// discovered by assembly scanning. Explicit registration means the set of
/// active modules is readable in one place, the order is deliberate, and a
/// module cannot activate itself by being copied into a directory (P9).
/// </para>
/// </summary>
public interface IPlatformModule
{
    /// <summary>
    /// Stable module name, lowercase, matching the database schema it owns —
    /// for example <c>identity</c>. Used in logs, metrics and audit events.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Registers the module's services. A module registers only its own
    /// services; it never reaches into another module's registration.
    /// </summary>
    void RegisterServices(IServiceCollection services, IConfiguration configuration);
}
