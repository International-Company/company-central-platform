using CCP.Modules.Authorization.Contracts;
using CCP.Modules.Configuration.Application.Abstractions;
using CCP.Modules.Organization.Contracts;

namespace CCP.Modules.Configuration.Infrastructure;

/// <summary>
/// Turns a user id into the roles and units a feature flag can be aimed at.
/// <para>
/// <b>This class is why Configuration does not reference Authorization or
/// Organization above the infrastructure layer.</b> The rule — what a flag may
/// be targeted by — lives in the domain; the lookup lives here, at the
/// composition root, where knowing about other modules is allowed (§6.2).
/// </para>
/// <para>
/// It reaches both modules through their narrow public surfaces and holds no
/// foreign key into either. Deliberately a second implementation rather than a
/// shared one with Documents: sharing would mean one module referencing the
/// other's Application layer, which is the coupling the architecture exists to
/// prevent. What <i>is</i> shared is <see cref="UnitPath"/>, because the path
/// format belongs to Organization and a third copy of it in this file would be a
/// third place to forget.
/// </para>
/// </summary>
public sealed class PlatformFeatureSubjectResolver(
    IRoleDirectory roles,
    IOrganizationDirectory organization) : IFeatureSubjectResolver
{
    public async Task<FeatureSubject> ResolveAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Guid> roleIds = await roles.GetRoleIdsForUserAsync(userId, cancellationToken);

        string? unitPath = await organization.GetUnitPathForUserAsync(userId, cancellationToken);

        return new FeatureSubject(roleIds, UnitPath.ParseChain(unitPath));
    }
}
