namespace CCP.Modules.Configuration.Application.Abstractions;

/// <summary>
/// Who is asking, in the only terms a feature flag can be aimed at.
/// </summary>
/// <param name="RoleIds">Every role the caller holds, at any scope.</param>
/// <param name="UnitChainIds">
/// Their unit and every unit above it. A flag aimed at a division reaches
/// somebody three levels down precisely when the division appears here.
/// </param>
public sealed record FeatureSubject(
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<Guid> UnitChainIds)
{
    /// <summary>
    /// A caller with no roles and no place in the company.
    /// <para>
    /// Untargeted flags still answer correctly for them; targeted ones do not
    /// reach them, which is the safe reading of "we do not know where you sit".
    /// </para>
    /// </summary>
    public static readonly FeatureSubject Bare = new([], []);
}

/// <summary>
/// Works out the caller's roles and unit ancestry.
/// <para>
/// <b>Declared here and implemented in infrastructure</b>, because answering it
/// means asking Authorization and Organization, and this module references
/// neither above that layer (§6.2). The same shape as Documents'
/// <c>IAccessSubjectResolver</c> and Workflow's assignee resolver, for the same
/// reason: the rule lives in the module, the lookup lives at the composition
/// root, and lifting the module into another product rewrites one file.
/// </para>
/// <para>
/// <b>Why it exists at all.</b> <c>GET /me/features/{key}</c> shipped in Phase 13
/// resolving neither, and passed two empty lists to an evaluator that reads them
/// — so every *targeted* flag answered "off" to everybody who asked through the
/// API. Untargeted flags worked, the Platform's own code worked because it
/// already knew the caller, and nothing failed anywhere. A rollout aimed at one
/// department simply never arrived, and the screen that asked had no way to
/// tell that from a flag which was genuinely off.
/// </para>
/// </summary>
public interface IFeatureSubjectResolver
{
    Task<FeatureSubject> ResolveAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The resolver when no directory is registered.
/// <para>
/// Answers <see cref="FeatureSubject.Bare"/>, which makes targeted flags
/// unreachable rather than universal. Registered by the module itself so a host
/// without Authorization or Organization still starts — and errs, as everything
/// here does, towards a feature being off.
/// </para>
/// </summary>
public sealed class BareFeatureSubjectResolver : IFeatureSubjectResolver
{
    public Task<FeatureSubject> ResolveAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult(FeatureSubject.Bare);
}
