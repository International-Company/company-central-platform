using CCP.Modules.Authorization.Contracts;
using CCP.Modules.Documents.Application.Abstractions;
using CCP.Modules.Organization.Contracts;

namespace CCP.Modules.Documents.Infrastructure;

/// <summary>
/// Turns a user id into the roles and units an access rule can name.
/// <para>
/// <b>This class is why Documents does not reference Organization or
/// Authorization above the infrastructure layer.</b> The rule — what a subject
/// is, and which rules reach them — lives in the Application layer; the lookup
/// lives here, at the composition root, where knowing about other modules is
/// allowed (§6.2). It is the same shape as Workflow's assignee resolver, for the
/// same reason: lift the module into another product and this is the one file
/// that has to be rewritten.
/// </para>
/// <para>
/// It reaches both modules through their narrow public surfaces —
/// <see cref="IOrganizationDirectory"/> and <see cref="IRoleDirectory"/> — never
/// their schemas, and holds no foreign key into either.
/// </para>
/// </summary>
public sealed class PlatformAccessSubjectResolver(
    IOrganizationDirectory organization,
    IRoleDirectory roles) : IAccessSubjectResolver
{
    public async Task<AccessSubject> ResolveAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Guid> roleIds = await roles.GetRoleIdsForUserAsync(userId, cancellationToken);

        string? unitPath = await organization.GetUnitPathForUserAsync(userId, cancellationToken);

        // Parsed by Organization's own helper rather than here. The format is
        // Organization's, and a copy of it in this file is a copy that keeps
        // compiling after Organization changes the separator -- visible only as
        // unit rules quietly reaching nobody.
        //
        // No employee record is normal, not exceptional: service accounts and
        // contractors sign in and have no place in the organization. The chain
        // comes back empty, so unit rules do not reach them, which is the safe
        // reading and the correct one.
        IReadOnlyList<Guid> chain = UnitPath.ParseChain(unitPath);

        return new AccessSubject(
            userId,
            roleIds,
            chain.Count > 0 ? chain[^1] : null,
            chain);
    }
}
