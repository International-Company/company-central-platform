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

    public WebAuthnOptions WebAuthn { get; set; } = new();

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

/// <summary>
/// Where passkeys are allowed to come from.
/// <para>
/// <b>None of this is a secret, and all of it is load-bearing.</b> A passkey is
/// bound by the browser to one domain, so these two values decide which pages
/// may ask for one. Get them wrong and nothing works; make them too broad and
/// the phishing resistance that is the point of a passkey is what you have
/// given away.
/// </para>
/// </summary>
public sealed class WebAuthnOptions
{
    /// <summary>
    /// The domain the credential belongs to, with no scheme and no port.
    /// <para>
    /// Baked into every passkey at registration and unchangeable afterwards:
    /// moving the Platform to another domain does not move the passkeys, and
    /// everybody enrols again. Worth deciding once, on the name the Platform
    /// will keep.
    /// </para>
    /// </summary>
    public string RelyingPartyId { get; set; } = "localhost";

    /// <summary>What the device calls this Platform when it asks the person.</summary>
    public string RelyingPartyName { get; set; } = "Company Central Platform";

    /// <summary>
    /// The full origins the sign-in page is served from, scheme and port
    /// included.
    /// <para>
    /// Checked exactly. A wildcard here would accept an assertion collected by
    /// any page on any subdomain, which is most of what a passkey exists to
    /// prevent.
    /// </para>
    /// </summary>
    public IList<string> Origins { get; set; } = ["http://localhost:3000"];

    /// <summary>
    /// How long a challenge stays answerable. Minutes, because a challenge
    /// nobody has answered by now is one nobody is going to, and every one
    /// still outstanding is one somebody could be working on.
    /// </summary>
    public TimeSpan ChallengeLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether a person may sign in with a passkey at all.
    /// <para>
    /// A switch rather than a belief. If something about this ever has to be
    /// turned off in a hurry, the alternative is a deployment.
    /// </para>
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Every origin must belong to the relying party, or no browser will hand
    /// over anything.
    /// <para>
    /// Checked at startup rather than discovered at the first sign-in. The
    /// symptom of a mismatch is the browser refusing with a message the server
    /// never sees, which is close to unattributable from the Platform's side.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> Misconfigurations()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(RelyingPartyId))
        {
            problems.Add("Identity:WebAuthn:RelyingPartyId is empty.");

            return problems;
        }

        if (RelyingPartyId.Contains("://", StringComparison.Ordinal) || RelyingPartyId.Contains(':'))
        {
            problems.Add(
                $"Identity:WebAuthn:RelyingPartyId is '{RelyingPartyId}'. It is a domain, with no scheme and no port.");
        }

        if (Origins.Count == 0)
        {
            problems.Add("Identity:WebAuthn:Origins is empty, so no page may ask for a passkey.");
        }

        foreach (string origin in Origins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? parsed))
            {
                problems.Add($"Identity:WebAuthn:Origins contains '{origin}', which is not an absolute URL.");

                continue;
            }

            bool belongs = string.Equals(parsed.Host, RelyingPartyId, StringComparison.OrdinalIgnoreCase)
                || parsed.Host.EndsWith("." + RelyingPartyId, StringComparison.OrdinalIgnoreCase);

            if (!belongs)
            {
                problems.Add(
                    $"Identity:WebAuthn:Origins contains '{origin}', whose host is not '{RelyingPartyId}' "
                    + "or a subdomain of it. No browser will return a passkey to it.");
            }

            // Everywhere but localhost, which browsers treat as secure so that
            // development is possible at all.
            if (parsed.Scheme != Uri.UriSchemeHttps
                && !string.Equals(parsed.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"Identity:WebAuthn:Origins contains '{origin}', which is not HTTPS.");
            }
        }

        return problems;
    }
}
