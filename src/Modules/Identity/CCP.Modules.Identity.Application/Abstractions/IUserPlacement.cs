namespace CCP.Modules.Identity.Application.Abstractions;

/// <summary>
/// Where in the organization the people behind these accounts sit.
/// <para>
/// <b>Identity has no organizational dimension, and this is how it stays that
/// way.</b> A user account is a way of signing in; it has no department. The
/// person behind it does, through an employee record the Organization module
/// owns — so "show me the users in my department" is a question Identity cannot
/// answer alone and must not learn to answer by growing a unit column of its
/// own, which would be a second place the answer lives and a second place it
/// goes stale.
/// </para>
/// <para>
/// Declared here and implemented in the infrastructure layer against
/// Organization's contract, the same shape Documents and Configuration use.
/// Nothing in this layer knows that the Organization module exists.
/// </para>
/// </summary>
public interface IUserPlacement
{
    /// <summary>
    /// The accounts of everyone whose employee record sits under any of these
    /// unit paths.
    /// <para>
    /// <b>A list of ids rather than a join, because no foreign key crosses a
    /// schema boundary.</b> It is a real cost — a company of ten thousand
    /// produces a ten-thousand-id filter — and it is the cost of a module that
    /// can be lifted out. The alternative is a join from <c>identity.users</c>
    /// into <c>organization.employees</c>, which is the one thing the
    /// architecture refuses.
    /// </para>
    /// <para>
    /// An account with no employee record is <b>absent</b>. That is the
    /// deliberate reading of what a scope means: an account with no place in the
    /// organization is in no department, so no department-scoped administrator
    /// sees it. The alternative — showing unplaced accounts to everybody — would
    /// show every service account to every unit administrator in the company.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Guid>> GetUserIdsUnderAsync(
        IReadOnlyCollection<string> unitPathPrefixes,
        CancellationToken cancellationToken = default);
}
