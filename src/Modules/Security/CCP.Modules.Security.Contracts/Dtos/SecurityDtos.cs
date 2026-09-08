namespace CCP.Modules.Security.Contracts.Dtos;

/// <summary>
/// What a user needs to enrol an authenticator.
/// <para>
/// <b>Returned exactly once.</b> The secret is never retrievable afterwards —
/// re-showing it would let anyone holding a stolen session clone the second
/// factor, which would make the factor pointless.
/// </para>
/// </summary>
/// <param name="ProvisioningUri">The <c>otpauth://</c> URI to render as a QR code.</param>
/// <param name="ManualEntryKey">
/// The base32 secret, for a user whose camera will not scan. Same secret, typed
/// rather than photographed.
/// </param>
public sealed record MfaEnrolmentDto(string ProvisioningUri, string ManualEntryKey);

/// <summary>
/// Recovery codes, shown once at enrolment or regeneration.
/// <para>
/// Stored hashed, so this response is the only moment they exist in readable
/// form. A client that does not put them in front of the user has lost them.
/// </para>
/// </summary>
public sealed record RecoveryCodesDto(IReadOnlyList<string> Codes, int Count);

/// <summary>The outcome of verifying a second factor.</summary>
/// <param name="Verified">Whether the code was accepted.</param>
/// <param name="UsedRecoveryCode">
/// Whether a recovery code was spent rather than a TOTP code. Surfaced so the
/// client can tell the user, who may not realise which they used.
/// </param>
/// <param name="RemainingRecoveryCodes">
/// How many are left, so the user can be warned before they run out and a lost
/// phone becomes an administrator's problem.
/// </param>
/// <param name="StepUpValidUntil">
/// Until when this confirmation satisfies step-up for sensitive operations.
/// </param>
public sealed record MfaVerificationDto(
    bool Verified,
    bool UsedRecoveryCode,
    int RemainingRecoveryCodes,
    DateTimeOffset StepUpValidUntil);

/// <summary>
/// The caller's MFA state.
/// <para>
/// Carries no secret and no code — only whether a factor exists and how healthy
/// it is. A status endpoint that leaked either would undo the enrolment flow's
/// show-once discipline.
/// </para>
/// </summary>
public sealed record MfaStatusDto(
    bool IsEnrolled,
    bool IsActive,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? LastUsedAt,
    int RemainingRecoveryCodes);

/// <summary>One security event, for the administration portal.</summary>
public sealed record SecurityEventDto(
    Guid Id,
    string EventType,
    string Severity,
    Guid? UserId,
    string? Username,
    string? IpAddress,
    string? Details,
    string? CorrelationId,
    DateTimeOffset OccurredAt);
