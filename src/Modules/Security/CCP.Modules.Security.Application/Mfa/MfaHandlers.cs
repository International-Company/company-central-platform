using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Security.Application.Abstractions;
using CCP.Modules.Security.Contracts.Dtos;
using CCP.Modules.Security.Domain;
using CCP.Modules.Security.Domain.Events;
using CCP.Modules.Security.Domain.Mfa;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Security.Application.Mfa;

/// <summary>Security module configuration.</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>Shown in the authenticator app, so a user with several accounts can tell them apart.</summary>
    public string TotpIssuer { get; set; } = "Company Central Platform";

    /// <summary>
    /// How many recovery codes are issued.
    /// <para>
    /// Ten: enough that losing a phone is recoverable more than once, few enough
    /// that a printout is not a large standing set of authentication bypasses.
    /// </para>
    /// </summary>
    public int RecoveryCodeCount { get; set; } = 10;

    /// <summary>Below this, the user is warned that they are running out.</summary>
    public int RecoveryCodeLowWaterMark { get; set; } = 3;

    /// <summary>
    /// How long a step-up confirmation lasts.
    /// <para>
    /// Fifteen minutes. Long enough to complete a run of administrative work
    /// without re-confirming constantly; short enough that a walked-away-from
    /// session cannot be used for the sensitive things step-up protects.
    /// </para>
    /// </summary>
    public TimeSpan StepUpValidity { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>Beginning MFA enrolment.</summary>
public sealed record BeginMfaEnrolmentCommand(Guid UserId, string Username);

/// <summary>
/// Issues a TOTP secret and the QR data for it.
/// <para>
/// The enrolment starts <b>pending</b>. It becomes a real second factor only
/// once the user has proven they can produce a code from it — activating on
/// issue would lock out anyone whose scan failed or whose phone clock is wrong,
/// with no way back in.
/// </para>
/// <para>
/// Starting a second enrolment discards any pending one. That is the recovery
/// path for a scan that went wrong: try again, rather than being stuck with a
/// half-finished enrolment.
/// </para>
/// </summary>
public sealed class BeginMfaEnrolmentHandler(
    ISecurityRepository repository,
    IMfaSecretProtector protector,
    ISecurityEventRecorder eventRecorder,
    ISecurityUnitOfWork unitOfWork,
    IClock clock,
    IOptions<SecurityOptions> options)
{
    private readonly SecurityOptions _options = options.Value;

    public async Task<Result<MfaEnrolmentDto>> HandleAsync(
        BeginMfaEnrolmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        MfaEnrolment? existing = await repository.FindEnrolmentAsync(command.UserId, cancellationToken);

        if (existing is { Status: MfaEnrolmentStatus.Active })
        {
            // Already protected. Re-enrolling would need the existing factor
            // proven first, which is what disable-then-enrol does.
            return Result.Failure<MfaEnrolmentDto>(SecurityErrors.MfaAlreadyActive);
        }

        if (existing is not null)
        {
            // A pending or disabled enrolment is replaced outright. Keeping the
            // old secret would leave a factor the user has abandoned.
            repository.RemoveEnrolment(existing);
        }

        byte[] secret = Totp.GenerateSecret();

        MfaEnrolment enrolment = MfaEnrolment.Begin(
            command.UserId, protector.Protect(secret), now);

        repository.AddEnrolment(enrolment);

        await eventRecorder.RecordAsync(
            SecurityEvent.Record(
                SecurityEventTypes.MfaEnrolmentStarted,
                SecuritySeverity.Informational,
                now,
                command.UserId,
                command.Username),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The secret is returned exactly once, here, so the user can scan it.
        // It is never retrievable afterwards — re-showing it would let anyone
        // with a stolen session clone the factor.
        return Result.Success(new MfaEnrolmentDto(
            Totp.BuildProvisioningUri(_options.TotpIssuer, command.Username, secret),
            Totp.Base32Secret(secret)));
    }
}

/// <summary>Confirming enrolment with a code from the authenticator.</summary>
public sealed record ConfirmMfaEnrolmentCommand(Guid UserId, string Username, string Code);

/// <summary>
/// Activates a pending enrolment once the user proves they hold the secret, and
/// issues recovery codes.
/// </summary>
public sealed class ConfirmMfaEnrolmentHandler(
    ISecurityRepository repository,
    IMfaSecretProtector protector,
    IRecoveryCodeGenerator recoveryCodeGenerator,
    ISecurityEventRecorder eventRecorder,
    ISecurityUnitOfWork unitOfWork,
    IClock clock,
    IOptions<SecurityOptions> options)
{
    private readonly SecurityOptions _options = options.Value;

    public async Task<Result<RecoveryCodesDto>> HandleAsync(
        ConfirmMfaEnrolmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        MfaEnrolment? enrolment = await repository.FindEnrolmentAsync(command.UserId, cancellationToken);

        if (enrolment is null)
        {
            return Result.Failure<RecoveryCodesDto>(SecurityErrors.MfaNotEnrolled);
        }

        if (enrolment.Status != MfaEnrolmentStatus.Pending)
        {
            return Result.Failure<RecoveryCodesDto>(SecurityErrors.EnrolmentNotPending);
        }

        byte[]? secret = protector.Unprotect(enrolment.EncryptedSecret);

        if (secret is null || !Totp.Verify(secret, command.Code, now))
        {
            enrolment.RecordFailure();

            await eventRecorder.RecordAsync(
                SecurityEvent.Record(
                    SecurityEventTypes.MfaChallengeFailed,
                    SecuritySeverity.Low,
                    now,
                    command.UserId,
                    command.Username,
                    details: """{"stage":"enrolment"}"""),
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<RecoveryCodesDto>(SecurityErrors.InvalidCode);
        }

        enrolment.Activate(now);

        (IReadOnlyList<string> codes, IReadOnlyList<string> hashes) =
            recoveryCodeGenerator.Generate(_options.RecoveryCodeCount);

        enrolment.ReplaceRecoveryCodes(hashes, now);

        await eventRecorder.RecordAsync(
            SecurityEvent.Record(
                SecurityEventTypes.MfaEnrolled,
                SecuritySeverity.Informational,
                now,
                command.UserId,
                command.Username),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Shown once. They are stored hashed, so this is the only moment they
        // exist in readable form — which the response makes explicit.
        return Result.Success(new RecoveryCodesDto(codes, codes.Count));
    }
}

/// <summary>Verifying a code at sign-in or for step-up.</summary>
public sealed record VerifyMfaCommand(
    Guid UserId,
    string Username,
    Guid SessionId,
    string Code,
    bool IsRecoveryCode,
    string? IpAddress);

/// <summary>
/// Verifies a second factor.
/// <para>
/// Accepts either a TOTP code or a recovery code, because at the moment someone
/// needs a recovery code they cannot produce a TOTP one — requiring a separate
/// endpoint would mean the client has to know in advance which the user will
/// reach for.
/// </para>
/// </summary>
public sealed class VerifyMfaHandler(
    ISecurityRepository repository,
    IMfaSecretProtector protector,
    IRecoveryCodeGenerator recoveryCodeGenerator,
    ISecurityEventRecorder eventRecorder,
    ISecurityUnitOfWork unitOfWork,
    IClock clock,
    IOptions<SecurityOptions> options)
{
    private readonly SecurityOptions _options = options.Value;

    public async Task<Result<MfaVerificationDto>> HandleAsync(
        VerifyMfaCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        MfaEnrolment? enrolment =
            await repository.FindActiveEnrolmentAsync(command.UserId, cancellationToken);

        if (enrolment is null)
        {
            return Result.Failure<MfaVerificationDto>(SecurityErrors.MfaNotActive);
        }

        // Checked before verifying, so a locked-out enrolment costs an attacker
        // nothing to discover and gains them nothing to keep trying.
        if (enrolment.IsLockedOut)
        {
            await RecordFailureAsync(
                enrolment, command, now, SecurityEventTypes.MfaLockedOut,
                SecuritySeverity.High, cancellationToken);

            return Result.Failure<MfaVerificationDto>(SecurityErrors.MfaLockedOut);
        }

        bool verified = command.IsRecoveryCode
            ? TryRedeemRecoveryCode(enrolment, command.Code, now)
            : VerifyTotp(enrolment, command.Code, now);

        if (!verified)
        {
            enrolment.RecordFailure();

            await RecordFailureAsync(
                enrolment, command, now, SecurityEventTypes.MfaChallengeFailed,
                SecuritySeverity.Medium, cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<MfaVerificationDto>(
                command.IsRecoveryCode ? SecurityErrors.InvalidRecoveryCode : SecurityErrors.InvalidCode);
        }

        enrolment.RecordSuccess(now);

        await eventRecorder.RecordAsync(
            SecurityEvent.Record(
                command.IsRecoveryCode
                    ? SecurityEventTypes.RecoveryCodeUsed
                    : SecurityEventTypes.MfaChallengeSucceeded,
                // A recovery code being used is worth more attention than an
                // ordinary success: it means the usual factor was unavailable,
                // which is either a lost phone or someone else's hands.
                command.IsRecoveryCode ? SecuritySeverity.Medium : SecuritySeverity.Informational,
                now,
                command.UserId,
                command.Username,
                command.IpAddress),
            cancellationToken);

        // The elevation is recorded here, bound to this session, rather than
        // returned as a claim the client holds. See StepUpConfirmation for why
        // that distinction matters.
        repository.AddStepUpConfirmation(
            StepUpConfirmation.Issue(
                command.UserId, command.SessionId, now, _options.StepUpValidity));

        int remaining = enrolment.RemainingRecoveryCodes;

        if (command.IsRecoveryCode && remaining <= _options.RecoveryCodeLowWaterMark)
        {
            await eventRecorder.RecordAsync(
                SecurityEvent.Record(
                    SecurityEventTypes.RecoveryCodesRunningLow,
                    SecuritySeverity.Low,
                    now,
                    command.UserId,
                    command.Username,
                    details: $$"""{"remaining":{{remaining}}}"""),
                cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new MfaVerificationDto(
            Verified: true,
            UsedRecoveryCode: command.IsRecoveryCode,
            RemainingRecoveryCodes: remaining,
            StepUpValidUntil: now.Add(_options.StepUpValidity)));
    }

    private bool VerifyTotp(MfaEnrolment enrolment, string code, DateTimeOffset now)
    {
        byte[]? secret = protector.Unprotect(enrolment.EncryptedSecret);

        return secret is not null && Totp.Verify(secret, code, now);
    }

    private bool TryRedeemRecoveryCode(MfaEnrolment enrolment, string code, DateTimeOffset now)
        => enrolment.RedeemRecoveryCode(recoveryCodeGenerator.Hash(code.Trim()), now) is not null;

    private async Task RecordFailureAsync(
        MfaEnrolment enrolment,
        VerifyMfaCommand command,
        DateTimeOffset now,
        string eventType,
        SecuritySeverity severity,
        CancellationToken cancellationToken)
        => await eventRecorder.RecordAsync(
            SecurityEvent.Record(
                eventType,
                severity,
                now,
                command.UserId,
                command.Username,
                command.IpAddress,
                details: $$"""{"failedAttempts":{{enrolment.FailedAttempts}}}"""),
            cancellationToken);
}

/// <summary>Turning the second factor off.</summary>
public sealed record DisableMfaCommand(Guid UserId, string Username, string Code);

/// <summary>
/// Disables MFA, after proving the factor being removed.
/// <para>
/// The current code is required. Without it, a stolen session could strip the
/// account's second factor — which is precisely the protection the session
/// should not be able to remove.
/// </para>
/// </summary>
public sealed class DisableMfaHandler(
    ISecurityRepository repository,
    IMfaSecretProtector protector,
    IRecoveryCodeGenerator recoveryCodeGenerator,
    ISecurityEventRecorder eventRecorder,
    ISecurityUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(
        DisableMfaCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        MfaEnrolment? enrolment =
            await repository.FindActiveEnrolmentAsync(command.UserId, cancellationToken);

        if (enrolment is null)
        {
            return Result.Failure(SecurityErrors.MfaNotActive);
        }

        byte[]? secret = protector.Unprotect(enrolment.EncryptedSecret);

        bool verified = (secret is not null && Totp.Verify(secret, command.Code, now))
                     || enrolment.RedeemRecoveryCode(
                            recoveryCodeGenerator.Hash(command.Code.Trim()), now) is not null;

        if (!verified)
        {
            enrolment.RecordFailure();

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure(SecurityErrors.InvalidCode);
        }

        enrolment.Disable(now);

        // Elevation granted by a factor that no longer exists must not outlive
        // it. Without this, disabling MFA would leave up to fifteen minutes of
        // privileged access standing on nothing.
        await repository.RevokeStepUpConfirmationsAsync(command.UserId, now, cancellationToken);

        // High severity. Removing a second factor is exactly what an attacker
        // does after taking an account, and it is the kind of change the real
        // owner should hear about immediately.
        await eventRecorder.RecordAsync(
            SecurityEvent.Record(
                SecurityEventTypes.MfaDisabled,
                SecuritySeverity.High,
                now,
                command.UserId,
                command.Username),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
