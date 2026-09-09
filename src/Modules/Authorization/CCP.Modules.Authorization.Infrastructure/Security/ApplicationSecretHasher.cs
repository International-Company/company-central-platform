using System.Security.Cryptography;
using System.Text;
using CCP.Modules.Authorization.Application.Abstractions;

namespace CCP.Modules.Authorization.Infrastructure.Security;

/// <summary>
/// Generates client credentials and checks them.
/// <para>
/// See <see cref="IApplicationSecretHasher"/> for why this is SHA-256 rather
/// than the password hash everyone reaches for. In one line: the secret has 256
/// bits of entropy from a cryptographic generator, so there is nothing to guess,
/// and a deliberately slow hash on the token endpoint would only give an
/// attacker with no credential at all a way to spend the server's CPU.
/// </para>
/// </summary>
public sealed class ApplicationSecretHasher : IApplicationSecretHasher
{
    /// <summary>
    /// Prefix on every client id, so one can be recognised on sight in a log, a
    /// configuration file, or a support ticket somebody pasted into a chat.
    /// </summary>
    private const string ClientIdPrefix = "ccp_";

    /// <summary>
    /// Prefix on every secret. Present for the same reason, and for one more:
    /// secret scanners look for recognisable prefixes, and a secret that looks
    /// like random base64 is a secret nobody notices in a commit.
    /// </summary>
    private const string SecretPrefix = "ccps_";

    public (string ClientId, string Secret, string SecretHash) Generate()
    {
        string clientId = ClientIdPrefix + Encode(RandomNumberGenerator.GetBytes(16));
        string secret = SecretPrefix + Encode(RandomNumberGenerator.GetBytes(32));

        return (clientId, secret, Hash(secret));
    }

    public string Hash(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }

    /// <summary>
    /// Constant time, on the hashes rather than on the secrets.
    /// <para>
    /// An ordinary string comparison returns sooner for a wrong first character
    /// than for a wrong last one. That difference is tiny and perfectly
    /// measurable across enough requests, and it turns a 256-bit secret into 32
    /// separate one-byte problems.
    /// </para>
    /// </summary>
    public bool Matches(string presentedSecret, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(presentedSecret) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Hash(presentedSecret)),
            Encoding.UTF8.GetBytes(storedHash));
    }

    /// <summary>
    /// URL-safe base64 without padding. A credential ends up in an environment
    /// variable, a header and occasionally a query string, and <c>+</c>, <c>/</c>
    /// and <c>=</c> each get mangled by something along the way.
    /// </summary>
    private static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}
