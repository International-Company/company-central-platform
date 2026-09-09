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
}
