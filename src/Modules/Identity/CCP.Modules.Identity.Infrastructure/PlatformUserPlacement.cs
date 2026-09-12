using CCP.Modules.Identity.Application.Abstractions;
using CCP.Modules.Organization.Contracts;

namespace CCP.Modules.Identity.Infrastructure;

/// <summary>
/// Answers Identity's question about where people sit, by asking Organization.
/// <para>
/// One line of delegation, and that is the point. The interface lives in
/// Identity's application layer so nothing there knows the Organization module
/// exists; the coupling is here, in the layer that composes modules, and it goes
/// through the public contract rather than into another schema.
/// </para>
/// </summary>
public sealed class PlatformUserPlacement(IOrganizationDirectory directory) : IUserPlacement
{
    public Task<IReadOnlyList<Guid>> GetUserIdsUnderAsync(
        IReadOnlyCollection<string> unitPathPrefixes,
        CancellationToken cancellationToken = default)
        => directory.GetUserIdsUnderAsync(unitPathPrefixes, cancellationToken);
}
