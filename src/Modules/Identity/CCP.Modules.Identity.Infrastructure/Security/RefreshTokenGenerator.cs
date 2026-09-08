using System.Security.Cryptography;
using System.Text;
using CCP.Modules.Identity.Application.Abstractions;

namespace CCP.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Generates opaque refresh tokens and hashes them for storage.
/// <para>
/// A refresh token is 256 bits of cryptographic randomness, not a signed
/// document. It carries no claims and means nothing on its own — it is only a
/// lookup key into server-side state, which is what makes it revocable
/// (ADR-006 §13.3).
/// </para>
/// <para>
/// Only the SHA-256 of the token is stored. SHA-256 rather than Argon2id here,
/// deliberately: the input is 256 bits of uniform randomness, so there is no
/// low-entropy guess space for an attacker to search, and the slow, memory-hard
/// hashing that passwords need would only add latency to every refresh. The
/// reasoning that makes Argon2id right for passwords is exactly what makes it
/// unnecessary here.
/// </para>
/// </summary>
public sealed class RefreshTokenGenerator : IRefreshTokenGenerator
{
    private const int TokenSizeBytes = 32;

    public (string Token, string Hash) Generate()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(TokenSizeBytes);

        // URL-safe base64 without padding: the token travels in headers, JSON
        // bodies and occasionally cookies, and must survive all three unaltered.
        string token = Base64UrlEncode(bytes);

        return (token, HashToken(token));
    }

    public string HashToken(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));

        return Convert.ToHexStringLower(hash);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
