using CCP.Modules.Authorization.Application.Abstractions;
using CCP.Modules.Organization.Contracts;

namespace CCP.Modules.Authorization.Infrastructure;

/// <summary>
/// Reads organizational facts through Organization's published contract.
/// <para>
/// A thin adapter, and deliberately thin. Authorization needs two facts from
/// Organization and gets them through <see cref="IOrganizationDirectory"/> —
/// never by touching the <c>organization</c> schema, which would couple the two
/// modules at the database and make either impossible to extract
/// (ARCHITECTURE.md §6.2).
/// </para>
/// <para>
/// The adapter exists rather than injecting <see cref="IOrganizationDirectory"/>
/// directly so that Authorization's Application layer depends on an interface it
/// owns. If Organization's contract changes shape, only this file moves.
/// </para>
/// </summary>
public sealed class OrganizationScopeReader(IOrganizationDirectory directory) : IOrganizationScopeReader
{
    public Task<string?> GetUnitPathAsync(Guid unitId, CancellationToken cancellationToken = default)
        => directory.GetUnitPathAsync(unitId, cancellationToken);

    public Task<string?> GetUnitPathForUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => directory.GetUnitPathForUserAsync(userId, cancellationToken);
}
