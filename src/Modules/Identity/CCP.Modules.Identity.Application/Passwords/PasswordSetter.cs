using CCP.Kernel.Application.Auditing;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Identity.Application.Abstractions;
using CCP.Modules.Identity.Domain.Credentials;
using CCP.Modules.Identity.Domain.Sessions;
using CCP.Modules.Identity.Domain.Users;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Identity.Application.Passwords;

/// <summary>
/// The one place a password is set.
/// <para>
/// Every route to a new password — an administrator creating an account, a user
/// changing their own, a reset redeemed by email — goes through here. Each of
/// those has to enforce the policy, screen against breaches, refuse reuse,
/// record history, prune it, and revoke other sessions. Left to three separate
/// handlers, one of them eventually forgets one of those steps, and the omission
/// is silent.
/// </para>
/// <para>
/// It does not commit. The calling handler owns the transaction, so the password
/// change and whatever else it does commit together.
/// </para>
/// </summary>
public sealed class PasswordSetter(
    IIdentityRepository repository,
    IPasswordHasher passwordHasher,
    IBreachedPasswordChecker breachedPasswordChecker,
    IAuditTrail auditTrail,
    IClock clock,
    IOptions<IdentityOptions> options)
{
    private readonly IdentityOptions _options = options.Value;

    /// <summary>
    /// Validates and applies a new password.
    /// </summary>
    /// <param name="user">The account. Must already be loaded.</param>
    /// <param name="newPassword">The proposed plaintext.</param>
    /// <param name="revokeOtherSessions">
    /// Whether to end the user's other sessions. True for a change or reset:
    /// if the old password was compromised, the sessions it opened may be too,
    /// and leaving them alive would make the change cosmetic.
    /// </param>
    /// <param name="currentSessionId">
    /// A session to keep, so a user changing their own password is not signed
    /// out of the tab they are using.
    /// </param>
    public async Task<Result> SetPasswordAsync(
        User user,
        string newPassword,
        bool revokeOtherSessions,
        Guid? currentSessionId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        DateTimeOffset now = clock.UtcNow;
        PasswordPolicy policy = _options.Password;

        // 1. Shape rules: length, and not containing the username.
        Result policyResult = policy.Validate(newPassword, user.Username);

        if (policyResult.IsFailure)
        {
            return policyResult;
        }

        // 2. Breach screening. The single most effective control available,
        //    because credential stuffing uses exactly these passwords.
        if (policy.CheckBreachedPasswords
            && await breachedPasswordChecker.IsBreachedAsync(newPassword, cancellationToken))
        {
            return Result.Failure(IdentityErrors.PasswordBreached);
        }

        // 3. Reuse. Checked against stored hashes, which means one Argon2id
        //    verification per retained entry — the reason history is short.
        if (policy.HistoryLength > 0
            && await IsRecentlyUsedAsync(user.Id, newPassword, policy.HistoryLength, cancellationToken))
        {
            return Result.Failure(IdentityErrors.PasswordReused);
        }

        // 4. Apply.
        string hash = passwordHasher.Hash(newPassword);

        UserCredential? credential = await repository.FindCredentialAsync(user.Id, cancellationToken);

        if (credential is null)
        {
            repository.AddCredential(
                UserCredential.Create(user.Id, hash, passwordHasher.AlgorithmId, now));
        }
        else
        {
            // The outgoing hash goes to history before it is overwritten;
            // afterwards it is gone and reuse could not be detected.
            repository.AddPasswordHistory(
                PasswordHistoryEntry.Create(user.Id, credential.PasswordHash, now));

            credential.Update(hash, passwordHasher.AlgorithmId, now);
        }

        await repository.PrunePasswordHistoryAsync(user.Id, policy.HistoryLength, cancellationToken);

        user.OnPasswordChanged(now);

        // 5. Any outstanding reset link must die with the old password. A link
        //    left valid in an inbox would let whoever holds it undo the change.
        await InvalidateOutstandingResetTokensAsync(user.Id, now, cancellationToken);

        // 6. End other sessions.
        if (revokeOtherSessions)
        {
            await RevokeOtherSessionsAsync(user.Id, currentSessionId, now, cancellationToken);
        }

        // The single choke point for every route to a new password, which makes
        // it the one place this can be recorded without a caller being able to
        // forget. No value is carried: the whole point of the field policy is
        // that a password never reaches the trail, redacted or otherwise.
        await auditTrail.RecordAsync(
            new AuditEntry(
                "identity",
                "password.changed",
                AuditOutcome.Success,
                "user",
                user.Id.ToString()),
            cancellationToken);

        return Result.Success();
    }

    private async Task<bool> IsRecentlyUsedAsync(
        Guid userId,
        string candidate,
        int historyLength,
        CancellationToken cancellationToken)
    {
        UserCredential? current = await repository.FindCredentialAsync(userId, cancellationToken);

        if (current is not null && passwordHasher.Verify(candidate, current.PasswordHash))
        {
            return true;
        }

        IReadOnlyList<PasswordHistoryEntry> history =
            await repository.GetPasswordHistoryAsync(userId, historyLength, cancellationToken);

        return history.Any(entry => passwordHasher.Verify(candidate, entry.PasswordHash));
    }

    private async Task InvalidateOutstandingResetTokensAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PasswordResetToken> outstanding =
            await repository.GetActivePasswordResetTokensAsync(userId, cancellationToken);

        foreach (PasswordResetToken token in outstanding)
        {
            token.Invalidate(now);
        }
    }

    private async Task RevokeOtherSessionsAsync(
        Guid userId,
        Guid? currentSessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Session> sessions =
            await repository.GetActiveSessionsAsync(userId, cancellationToken);

        foreach (Session session in sessions)
        {
            if (session.Id == currentSessionId)
            {
                continue;
            }

            session.Revoke(SessionRevocationReasons.PasswordChanged, now);

            IReadOnlyList<RefreshToken> tokens =
                await repository.GetSessionRefreshTokensAsync(session.Id, cancellationToken);

            foreach (RefreshToken token in tokens)
            {
                token.Revoke(SessionRevocationReasons.PasswordChanged, now);
            }
        }
    }
}
