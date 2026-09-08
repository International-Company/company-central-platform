using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Application.Events;
using CCP.Modules.Security.Domain.Events;
using CCP.Modules.Security.Domain.Mfa;

namespace CCP.Modules.Security.Application.Abstractions;

/// <summary>The Security module's unit of work.</summary>
public interface ISecurityUnitOfWork : IUnitOfWork;

/// <summary>The Security module's outbox, bound to its own DbContext.</summary>
public interface ISecurityOutbox : IOutbox;

/// <summary>
/// Encrypts and decrypts TOTP secrets.
/// <para>
/// An interface so the domain and use cases never hold key material, and so the
/// key source can change — a cloud KMS instead of a mounted file — without
/// touching anything that uses it.
/// </para>
/// </summary>
public interface IMfaSecretProtector
{
    /// <summary>Encrypts a secret for storage.</summary>
    string Protect(byte[] secret);

    /// <summary>
    /// Decrypts a stored secret, or returns null if it cannot be authenticated.
    /// <para>
    /// Null rather than an exception, because the caller's correct response is
    /// always the same: deny the factor. A wrong key and tampered ciphertext are
    /// indistinguishable from the outside and need no distinction.
    /// </para>
    /// </summary>
    byte[]? Unprotect(string protectedSecret);
}

/// <summary>Generates and hashes recovery codes.</summary>
public interface IRecoveryCodeGenerator
{
    /// <summary>
    /// Creates a set of codes, returning the plaintext to show the user once and
    /// the hashes to store.
    /// <para>
    /// Both together, because it is the only moment both exist. The plaintext is
    /// never derivable afterwards, which is the point.
    /// </para>
    /// </summary>
    (IReadOnlyList<string> Codes, IReadOnlyList<string> Hashes) Generate(int count);

    /// <summary>Hashes a presented code for lookup.</summary>
    string Hash(string code);
}

/// <summary>
/// Records security events.
/// <para>
/// Separate from the outbox so that recording an event is a direct, deliberate
/// act rather than a side effect. Detection depends on these being written even
/// when the surrounding operation fails — a failed sign-in is exactly the case
/// where nothing else commits.
/// </para>
/// </summary>
public interface ISecurityEventRecorder
{
    Task RecordAsync(SecurityEvent securityEvent, CancellationToken cancellationToken = default);
}

/// <summary>Persistence for the Security module.</summary>
public interface ISecurityRepository
{
    Task<MfaEnrolment?> FindEnrolmentAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<MfaEnrolment?> FindActiveEnrolmentAsync(Guid userId, CancellationToken cancellationToken = default);

    void AddEnrolment(MfaEnrolment enrolment);

    void RemoveEnrolment(MfaEnrolment enrolment);

    Task AddSecurityEventAsync(SecurityEvent securityEvent, CancellationToken cancellationToken = default);

    /// <summary>Records that a user proved their second factor on a session.</summary>
    void AddStepUpConfirmation(StepUpConfirmation confirmation);

    /// <summary>
    /// Whether this session currently holds a valid step-up confirmation.
    /// <para>
    /// Asked on every request to a step-up-protected endpoint, so it is a single
    /// indexed lookup rather than a scan: the index leads with the session.
    /// </para>
    /// </summary>
    Task<bool> HasValidStepUpAsync(
        Guid userId, Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends every live elevation for a user.
    /// <para>
    /// Called when the factor itself changes. Disabling MFA while an elevation
    /// is outstanding would otherwise leave the elevation running on a factor
    /// that no longer exists.
    /// </para>
    /// </summary>
    Task<int> RevokeStepUpConfirmationsAsync(
        Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<SecurityEvent> Items, long TotalCount)> SearchSecurityEventsAsync(
        Guid? userId,
        string? eventType,
        SecuritySeverity? minimumSeverity,
        DateTimeOffset from,
        DateTimeOffset to,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
}
