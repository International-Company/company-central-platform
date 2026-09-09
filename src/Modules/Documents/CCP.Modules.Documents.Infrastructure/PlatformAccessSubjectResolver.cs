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

        // No employee record is normal, not exceptional: service accounts and
        // contractors sign in and have no place in the organization. They get
        // their roles and no unit, which means unit rules do not reach them —
        // the safe reading, and the correct one.
        if (string.IsNullOrEmpty(unitPath))
        {
            return new AccessSubject(userId, roleIds, null, []);
        }

        // The materialized path is built from unit ids, so the caller's whole
        // ancestry is already in the string and costs nothing to read. Asking
        // Organization to walk the tree would be a query per level, on every
        // request, for something the path was designed to make free.
        List<Guid> chain = ParseChain(unitPath);

        return new AccessSubject(
            userId,
            roleIds,
            chain.Count > 0 ? chain[^1] : null,
            chain);
    }

    /// <summary>
    /// <c>/{id}/{id}/…</c> into ids, nearest last.
    /// <para>
    /// A segment that will not parse is skipped rather than thrown over. A
    /// corrupted path should cost somebody a unit rule, not the ability to open
    /// any document at all.
    /// </para>
    /// </summary>
    private static List<Guid> ParseChain(string unitPath)
    {
        string[] segments = unitPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        List<Guid> chain = new(segments.Length);

        foreach (string segment in segments)
        {
            if (Guid.TryParse(segment, out Guid id))
            {
                chain.Add(id);
            }
        }

        return chain;
    }
}
