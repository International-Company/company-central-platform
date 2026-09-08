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
/// The stored form is <c>nonce | tag | ciphertext</c>, base64. A fresh random
/// nonce per encryption is essential — reusing one with GCM does not merely
/// weaken it, it reveals the key stream and lets an attacker forge.
/// </para>
/// </summary>
public sealed class MfaSecretProtector : IMfaSecretProtector, IDisposable
{
    private const int NonceSize = 12;  // 96 bits, the value AES-GCM is defined for
    private const int TagSize = 16;    // 128 bits, the full authentication tag
    private const int KeySize = 32;    // 256 bits

    private readonly byte[] _key;

    public MfaSecretProtector(
        IOptions<MfaProtectionOptions> options,
        IHostEnvironment environment,
        ILogger<MfaSecretProtector> logger)
    {
        ArgumentNullException.ThrowIfNull(environment);

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
                "Security:MfaProtection:KeyPath is not configured. A key must be supplied "
                + "explicitly outside Development; the Platform will not generate one, because "
                + "a generated key would change on every deployment and make every enrolled "
                + "second factor unusable.");
        }

        _key = RandomNumberGenerator.GetBytes(KeySize);

        logger.LogWarning(
            "No MFA protection key configured. An ephemeral development key has been generated. "
            + "Every enrolled second factor becomes unusable when this process restarts. "
            + "This is permitted in Development only.");
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

        return Convert.ToBase64String(combined);
    }

    public byte[]? Unprotect(string protectedSecret)
    {
        if (string.IsNullOrWhiteSpace(protectedSecret))
        {
            return null;
        }

        byte[] combined;

        try
        {
            combined = Convert.FromBase64String(protectedSecret);
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
            using var aes = new AesGcm(_key, TagSize);
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

    public void Dispose() => CryptographicOperations.ZeroMemory(_key);
}
