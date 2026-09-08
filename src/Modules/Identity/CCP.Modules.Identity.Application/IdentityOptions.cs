using CCP.Modules.Identity.Domain.Credentials;
using CCP.Modules.Identity.Domain.Users;

namespace CCP.Modules.Identity.Application;

/// <summary>
/// Identity configuration. Bound from the <c>Identity</c> section and validated
/// at startup, so a misconfigured deployment fails immediately rather than at
/// the first sign-in attempt.
/// <para>
/// Lifetimes follow ADR-006 §13.3: a short access token limits the window of a
/// leak, while a longer, rotating, revocable refresh token keeps people from
/// having to sign in constantly.
/// </para>
/// </summary>
public sealed class IdentityOptions
{
    public const string SectionName = "Identity";

    /// <summary>
    /// How long an access token is valid. Short by design — an access token
    /// cannot be revoked before it expires, so its lifetime *is* the revocation
    /// delay for anything that relies on it.
    /// </summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How long a refresh token is valid. Longer, because it is revocable,
    /// rotates on every use, and reuse is detected.
    /// </summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    /// The hard ceiling on a session. It ends at this point however active it
    /// has been, so no session lives forever through continuous use.
    /// </summary>
    public TimeSpan SessionAbsoluteLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// How long a session may sit idle before it stops being usable. Shorter
    /// than the absolute lifetime, and the control that matters for an
    /// unattended browser.
    /// </summary>
    public TimeSpan SessionIdleTimeout { get; set; } = TimeSpan.FromHours(12);

    /// <summary>How long a password reset token is valid. Deliberately brief.</summary>
    public TimeSpan PasswordResetTokenLifetime { get; set; } = TimeSpan.FromMinutes(30);

    public PasswordPolicy Password { get; set; } = PasswordPolicy.Default;

    public LockoutPolicy Lockout { get; set; } = LockoutPolicy.Default;

    /// <summary>JWT issuer, validated on every token.</summary>
    public string Issuer { get; set; } = "https://platform.company.com";

    /// <summary>JWT audience, validated on every token.</summary>
    public string Audience { get; set; } = "ccp-api";

    /// <summary>
    /// Path to the RSA private key used to sign tokens.
    /// <para>
    /// A path, not a key. The key material itself comes from the secret manager
    /// or a mounted file and never appears in configuration, in the repository,
    /// or in this object (ARCHITECTURE.md §12.7).
    /// </para>
    /// </summary>
    public string? SigningKeyPath { get; set; }

    /// <summary>
    /// The RSA private key itself, as PEM or as base64-encoded PEM.
    /// <para>
    /// <b>A deliberate, documented weakening of the rule above, and the reason
    /// is worth stating plainly.</b> A path keeps key material out of the
    /// process environment, where it is visible to anything that can read
    /// <c>/proc</c>, appears in crash dumps and container inspection output, and
    /// is printed by any diagnostic that dumps configuration. That is strictly
    /// better, and it remains the preferred form.
    /// </para>
    /// <para>
    /// But several managed platforms offer no mounted files at all — a secret
    /// there is an environment variable or it does not exist. Refusing to read
    /// one would not make those deployments more secure; it would make them
    /// impossible, and the realistic outcome of that is a key committed to Git
    /// by someone in a hurry. This is the lesser risk, taken knowingly.
    /// </para>
    /// <para>
    /// <see cref="SigningKeyPath"/> wins when both are set.
    /// </para>
    /// </summary>
    public string? SigningKey { get; set; }
}
