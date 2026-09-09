namespace CCP.Modules.Authorization.Contracts;

/// <summary>
/// The Authorization module's public surface for other modules.
/// <para>
/// One question, because one is all anybody needs: <b>who holds this role?</b>
/// Workflow asks it to turn "whoever approves purchases" into people with
/// inboxes (ARCHITECTURE.md §6.2, §16.3).
/// </para>
/// <para>
/// Deliberately not "what may this person do" — that is
/// <c>IPermissionResolver</c>, which lives in the Application layer and is the
/// Platform's own concern. A module that could ask arbitrary authorization
/// questions would end up making authorization decisions of its own, and there
/// would then be two places where access is decided.
/// </para>
/// </summary>
public interface IRoleDirectory
{
    /// <summary>
    /// The user accounts holding a role, at any scope, whose grant is live.
    /// <para>
    /// Scope is not filtered here. A role granted for one department still
    /// makes its holder a candidate assignee, and narrowing by scope would mean
    /// this interface understanding what the task is about — which is exactly
    /// the business knowledge the workflow engine refuses to hold.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Guid>> GetUserIdsWithRoleAsync(
        Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The roles one person holds, at any scope, whose grant is live.
    /// <para>
    /// The same question from the other end, and it earns its place because
    /// the alternative is worse. Documents may be shared with a role, and
    /// deciding whether a caller is in one of the roles named on a document
    /// through <see cref="GetUserIdsWithRoleAsync"/> would mean fetching the
    /// full membership of every role mentioned — on every request, to answer a
    /// question about one person.
    /// </para>
    /// <para>
    /// Still not "what may this person do". These are role identifiers, not
    /// permissions: a caller can compare them against rules it owns and cannot
    /// use them to make an authorization decision of its own.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Guid>> GetRoleIdsForUserAsync(
        Guid userId, CancellationToken cancellationToken = default);
}
