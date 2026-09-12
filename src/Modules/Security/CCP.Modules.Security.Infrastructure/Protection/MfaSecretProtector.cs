using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using CCP.Modules.Security.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Security.Infrastructure.Protection;

/// <summary>Where the MFA encryption key comes from.</summary>
public sealed class MfaProtectionOptions
{
    public const string SectionName = "Security:MfaProtection";

    /// <summary>
    /// Path to a file containing the 256-bit key, base64-encoded.
    /// <para>
    /// <b>A path, not a key.</b> The key material never appears in
    /// configuration or in the repository; the file comes from the secret
    /// manager or a mounted secret (ARCHITECTURE.md §12.7).
    /// </para>
    /// </summary>
    [MaxLength(4096)]
    public string? KeyPath { get; set; }

    /// <summary>
    /// The 256-bit key itself, base64-encoded.
    /// <para>
    /// <b>A deliberate, documented weakening of the rule above.</b> A path keeps
    /// key material out of the process environment, where it is visible to
    /// anything that can read <c>/proc</c>, appears in crash dumps and container
    /// inspection output, and is printed by any diagnostic that dumps
    /// configuration. That remains the preferred form.
    /// </para>
    /// <para>
    /// Several managed platforms offer no mounted files at all — a secret there
    /// is an environment variable or it does not exist. Refusing to read one
    /// would not make those deployments safer, only impossible, and the
    /// realistic outcome of that is a key committed to Git by someone in a
    /// hurry. This is the lesser risk, taken knowingly.
    /// </para>
    /// <para><see cref="KeyPath"/> wins when both are set.</para>
    /// </summary>
    [MaxLength(512)]
    public string? Key { get; set; }

    /// <summary>
    /// A short name for the key currently in use, written into everything it
    /// encrypts.
    /// <para>
    /// <b>Without this, rotation is impossible rather than merely awkward.</b> A
    /// stored secret that does not say which key produced it can only be read by
    /// trying every key, and can never be reasoned about — so replacing a key
    /// meant making every enrolled second factor undecryptable, which is to say
    /// the key could never be replaced at all. That is a poor position for the
    /// one key in the Platform that protects a second factor.
    /// </para>
    /// <para>
    /// Any short label: <c>1</c>, <c>2026-09</c>, whatever the rotation
    /// procedure finds meaningful. It must not contain a full stop, which
    /// separates the fields of the stored form.
    /// </para>
    /// </summary>
    [MaxLength(64)]
    public string ActiveKeyId { get; set; } = "1";

    /// <summary>
    /// Keys that are no longer used for encryption and are still needed to read
    /// what they encrypted.
    /// <para>
    /// A rotation is not an instant: the new key takes over immediately and the
    /// old secrets stay as they are until each is re-encrypted. Retiring a key
    /// here rather than deleting it is what makes the period between those two
    /// things survivable.
    /// </para>
    /// </summary>
    public IList<RetiredMfaKey> RetiredKeys { get; } = [];
}

/// <summary>
/// A key kept only for reading.
/// <para>
/// The same choice between a path and a value as the active key, for the same
/// reasons, and with the same preference.
/// </para>
/// </summary>
public sealed class RetiredMfaKey
{
    /// <summary>The name this key was known by when it was in use.</summary>
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    /// <summary>Path to the file holding it, base64-encoded. Preferred.</summary>
    [MaxLength(4096)]
    public string? KeyPath { get; set; }

    /// <summary>The key itself, base64-encoded.</summary>
    [MaxLength(512)]
    public string? Key { get; set; }
}

/// <summary>
/// Encrypts TOTP secrets at rest with AES-256-GCM.
/// <para>
/// <b>Why encryption and not hashing.</b> A password is verified by hashing the
/// candidate and comparing, so the original is never needed. A TOTP secret is
/// different: computing the expected code requires the secret itself, so it must
/// be recoverable. That is a genuinely weaker position, and the mitigation is
/// that the key lives outside the database — a leaked backup yields ciphertext,
/// not working second factors.
/// </para>
/// <para>
/// AES-GCM rather than AES-CBC: it authenticates as well as encrypts, so
/// tampered ciphertext fails to decrypt instead of silently producing a wrong
/// secret. Encryption without authentication is a well-worn way to build
/// something that looks secure and is not.
/// </para>
/// <para>
/// The stored form is <c>v2.{keyId}.{base64(nonce | tag | ciphertext)}</c>. A
/// fresh random nonce per encryption is essential — reusing one with GCM does not
/// merely weaken it, it reveals the key stream and lets an attacker forge.
/// </para>
/// <para>
/// <b>The version lives in a prefix rather than in the bytes, and that is the
/// whole reason this could be added at all.</b> Secrets already stored are bare
/// base64 of <c>nonce | tag | ciphertext</c> with nothing to say so. Prepending a
/// version byte to the plaintext form would have made every one of them
/// unreadable — the precise outage this change exists to make possible to avoid.
/// A prefix is unambiguous: a value that does not start with <c>v2.</c> predates
/// versioning and is read with the active key, which is the only key it can have
/// been written with.
/// </para>
/// </summary>
public sealed class MfaSecretProtector : IMfaSecretProtector, IDisposable
{
    private const int NonceSize = 12;  // 96 bits, the value AES-GCM is defined for
    private const int TagSize = 16;    // 128 bits, the full authentication tag
    private const int KeySize = 32;    // 256 bits

    /// <summary>The marker that says a stored value names its key.</summary>
    private const string VersionPrefix = "v2";

    private readonly byte[] _key;
    private readonly string _activeKeyId;

    /// <summary>
    /// Keys kept only for reading, by the name they were written under.
    /// <para>
    /// A rotation is not an instant. The new key takes over immediately and the
    /// secrets already stored stay as they are, so both have to be readable for
    /// as long as any of the old ones remain.
    /// </para>
    /// </summary>
    private readonly Dictionary<string, byte[]> _retiredKeys = new(StringComparer.Ordinal);

    public MfaSecretProtector(
        IOptions<MfaProtectionOptions> options,
        IHostEnvironment environment,
        ILogger<MfaSecretProtector> logger)
    {
        ArgumentNullException.ThrowIfNull(environment);

        _activeKeyId = (options.Value.ActiveKeyId ?? string.Empty).Trim();

        if (_activeKeyId.Length == 0 || _activeKeyId.Contains('.', StringComparison.Ordinal))
        {
            // The full stop separates the fields of the stored form, so a key id
            // containing one would produce values that cannot be read back --
            // discovered on the day of a rotation, which is the worst day.
            throw new InvalidOperationException(
                "Security:MfaProtection:ActiveKeyId must be a non-empty label with no full stop.");
        }

        foreach (RetiredMfaKey retired in options.Value.RetiredKeys)
        {
            string id = (retired.Id ?? string.Empty).Trim();

            if (id.Length == 0)
            {
                throw new InvalidOperationException(
                    "A retired MFA key has no Id. Without one, nothing can say which stored "
                    + "secrets it reads.");
            }

            if (string.Equals(id, _activeKeyId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The MFA key '{id}' is listed as retired and is also the active key. "
                    + "One of the two is a mistake, and guessing which would be worse than "
                    + "refusing to start.");
            }

            _retiredKeys[id] = ReadKey(
                retired.Key, retired.KeyPath, $"the retired MFA key '{id}'");
        }

        string? inlineKey = options.Value.Key;

        if (!string.IsNullOrWhiteSpace(inlineKey)
            && !inlineKey.StartsWith("REPLACE_WITH", StringComparison.Ordinal))
        {
            byte[] supplied;

            try
            {
                supplied = Convert.FromBase64String(inlineKey.Trim());
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException(
                    "Security:MfaProtection:Key is not valid base64.", exception);
            }

            if (supplied.Length != KeySize)
            {
                throw new InvalidOperationException(
                    $"Security:MfaProtection:Key is {supplied.Length * 8} bits. "
                    + "It must be exactly 256 bits.");
            }

            _key = supplied;

            return;
        }

        string? path = options.Value.KeyPath;

        if (!string.IsNullOrWhiteSpace(path) && !path.StartsWith("REPLACE_WITH", StringComparison.Ordinal))
        {
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"The MFA protection key was not found at '{path}'. "
                    + "Set Security:MfaProtection:KeyPath to a readable file containing a "
                    + "base64-encoded 256-bit key.");
            }

            byte[] key;

            try
            {
                key = Convert.FromBase64String(File.ReadAllText(path).Trim());
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException(
                    $"The MFA protection key at '{path}' is not valid base64.", exception);
            }

            if (key.Length != KeySize)
            {
                throw new InvalidOperationException(
                    $"The MFA protection key at '{path}' is {key.Length * 8} bits. "
                    + "It must be exactly 256 bits.");
            }

            _key = key;

            return;
        }

        if (!environment.IsDevelopment())
        {
            // Generating one silently would be worse than failing: every enrolled
            // second factor would become undecryptable on the next deployment,
            // and every user would be locked out with no explanation.
            throw new InvalidOperationException(
                "Neither Security:MfaProtection:KeyPath nor Security:MfaProtection:Key is "
                + "configured. A key must be supplied explicitly outside Development; the Platform "
                + "will not generate one, because a generated key would change on every deployment "
                + "and make every enrolled second factor unusable. Prefer the path; use the key "
                + "itself only where the platform offers no mounted files.");
        }

        _key = RandomNumberGenerator.GetBytes(KeySize);

        logger.LogWarning(
            "No MFA protection key configured. An ephemeral development key has been generated. "
            + "Every enrolled second factor becomes unusable when this process restarts. "
            + "This is permitted in Development only.");
    }

    /// <summary>
    /// Reads a 256-bit key from a value or a path, and says which key it was
    /// complaining about.
    /// <para>
    /// Named in the message because a deployment with three keys configured and
    /// a message saying "the key is not valid base64" is a deployment somebody
    /// debugs by deleting things.
    /// </para>
    /// </summary>
    private static byte[] ReadKey(string? value, string? path, string description)
    {
        string? material = value;

        if (!string.IsNullOrWhiteSpace(path))
        {
            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"{description} was not found at '{path}'.");
            }

            material = File.ReadAllText(path);
        }

        if (string.IsNullOrWhiteSpace(material))
        {
            throw new InvalidOperationException(
                $"{description} has neither a Key nor a readable KeyPath.");
        }

        byte[] key;

        try
        {
            key = Convert.FromBase64String(material.Trim());
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException($"{description} is not valid base64.", exception);
        }

        if (key.Length != KeySize)
        {
            throw new InvalidOperationException(
                $"{description} is {key.Length * 8} bits. It must be exactly 256 bits.");
        }

        return key;
    }

    public string Protect(byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] ciphertext = new byte[secret.Length];
        byte[] tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, secret, ciphertext, tag);

        byte[] combined = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(combined, 0);
        tag.CopyTo(combined, NonceSize);
        ciphertext.CopyTo(combined, NonceSize + TagSize);

        // Always with the active key, and always saying which. A stored secret
        // that cannot name its key can only be read by trying every one, and can
        // never be reasoned about.
        return $"{VersionPrefix}.{_activeKeyId}.{Convert.ToBase64String(combined)}";
    }

    public byte[]? Unprotect(string protectedSecret)
    {
        if (string.IsNullOrWhiteSpace(protectedSecret))
        {
            return null;
        }

        // A value that does not name a version predates versioning, and the only
        // key it can have been written with is the one in use at the time --
        // which is the active key, since rotation was impossible before this.
        byte[] key = _key;
        string payload = protectedSecret;

        if (protectedSecret.StartsWith(VersionPrefix + ".", StringComparison.Ordinal))
        {
            string[] parts = protectedSecret.Split('.', 3);

            if (parts.Length != 3)
            {
                return null;
            }

            if (!string.Equals(parts[1], _activeKeyId, StringComparison.Ordinal)
                && !_retiredKeys.TryGetValue(parts[1], out key!))
            {
                // Written under a key this deployment does not hold. Denying the
                // factor is the only safe answer, and it is a configuration
                // problem rather than a tampered secret -- so it must not look
                // like one in the log.
                return null;
            }

            payload = parts[2];
        }

        byte[] combined;

        try
        {
            combined = Convert.FromBase64String(payload);
        }
        catch (FormatException)
        {
            return null;
        }

        if (combined.Length < NonceSize + TagSize)
        {
            return null;
        }

        byte[] nonce = combined[..NonceSize];
        byte[] tag = combined[NonceSize..(NonceSize + TagSize)];
        byte[] ciphertext = combined[(NonceSize + TagSize)..];
        byte[] plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            return plaintext;
        }
        catch (CryptographicException)
        {
            // Authentication failed: the ciphertext was tampered with, or the
            // key has changed. Returning null denies the factor rather than
            // proceeding with a wrong secret — which would reject every code the
            // user produced and look like a broken authenticator.
            return null;
        }
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_key);

        foreach (byte[] retired in _retiredKeys.Values)
        {
            CryptographicOperations.ZeroMemory(retired);
        }
    }
}
