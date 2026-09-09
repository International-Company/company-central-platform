using System.Text.RegularExpressions;

namespace CCP.Modules.Configuration.Domain.Settings;

/// <summary>
/// Refuses a value that looks like a secret, wherever one is offered.
/// <para>
/// <b>The configuration/secret split is the whole point of this class.</b>
/// Settings are stored in plaintext in a table many people can read, exported,
/// backed up and shown on a screen. A secret put there is a secret in all of
/// those places, and the person who put it there did so because it was
/// convenient — which is exactly when it happens.
/// </para>
/// <para>
/// Marking a setting sensitive does not make it a safe home for one. Sensitivity
/// stops a value being read back; it does not stop it being in the database, in
/// the backup, or in the hands of whoever gets a copy. Secrets live in the secret
/// manager and are named by reference (§19.3, §21.1).
/// </para>
/// <para>
/// <b>This is a guard rail and says so.</b> It catches the recognisable
/// paste — a private key, a bearer token, a connection string with a password,
/// a long high-entropy string — and it will not catch a short password somebody
/// typed. A heuristic that tried to would refuse half the legitimate values in
/// the Platform. The rest is a review and a documented rule.
/// </para>
/// </summary>
public static partial class SecretShapedValue
{
    /// <summary>
    /// How long a value has to be before its randomness is worth measuring.
    /// <para>
    /// Below this, high entropy is normal: a locale, a hex colour, a short code.
    /// Above it, a run of random-looking characters is almost always a key.
    /// </para>
    /// </summary>
    private const int EntropyThreshold = 32;

    /// <summary>
    /// Openings that are never anything but a credential.
    /// </summary>
    private static readonly string[] Prefixes =
    [
        "-----BEGIN",       // any PEM: private key, certificate
        "sk-",              // several vendors' secret keys
        "ccps_",            // this Platform's own client secrets
        "AKIA",             // AWS access key id
        "ASIA",             // AWS temporary access key id
        "ghp_", "gho_", "ghs_",  // GitHub tokens
        "xoxb-", "xoxp-",   // Slack tokens
        "Bearer ",
        "Basic "
    ];

    /// <summary>
    /// Whether this looks like something that belongs in the secret manager.
    /// </summary>
    public static bool Looks(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();

        foreach (string prefix in Prefixes)
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // A connection string carrying a password. These are pasted into
        // settings more often than anything else on this list, because they look
        // like configuration — and they are, apart from the part that is not.
        if (CredentialInConnectionString().IsMatch(trimmed))
        {
            return true;
        }

        // A JSON Web Token. Three base64 segments and a header that decodes to
        // something announcing itself.
        if (trimmed.StartsWith("eyJ", StringComparison.Ordinal) && trimmed.Count(c => c == '.') == 2)
        {
            return true;
        }

        return LooksRandom(trimmed);
    }

    /// <summary>
    /// A long run of characters with no structure a person would produce.
    /// <para>
    /// Deliberately crude: length, a full alphabet, and no spaces. A sentence
    /// has spaces, a URL has punctuation in predictable places, a path has
    /// separators — and a generated key has none of that and uses upper case,
    /// lower case and digits throughout.
    /// </para>
    /// </summary>
    private static bool LooksRandom(string value)
    {
        if (value.Length < EntropyThreshold || value.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        // A URL is long, unbroken and legitimate. It is also a setting type of
        // its own, so refusing one here would refuse the thing the Url type
        // exists for.
        if (Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            return false;
        }

        bool hasUpper = false;
        bool hasLower = false;
        bool hasDigit = false;

        foreach (char character in value)
        {
            hasUpper |= char.IsAsciiLetterUpper(character);
            hasLower |= char.IsAsciiLetterLower(character);
            hasDigit |= char.IsAsciiDigit(character);
        }

        return hasUpper && hasLower && hasDigit;
    }

    [GeneratedRegex(
        @"(password|pwd|secret|api[_-]?key)\s*=\s*[^;\s]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex CredentialInConnectionString();
}
