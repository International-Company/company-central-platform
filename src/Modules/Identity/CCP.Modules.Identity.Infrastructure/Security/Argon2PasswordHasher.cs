using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CCP.Modules.Identity.Application.Abstractions;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Argon2id password hashing (ARCHITECTURE.md §12.3).
/// <para>
/// Argon2id rather than PBKDF2 or bcrypt because it is <i>memory-hard</i>: the
/// work cannot be made cheap by throwing GPUs or ASICs at it, which is precisely
/// how modern offline cracking works. It won the Password Hashing Competition
/// and is the current recommendation.
/// </para>
/// <para>
/// Hashes are stored in a self-describing format:
/// <c>$argon2id$v=19$m=65536,t=3,p=2$&lt;salt&gt;$&lt;hash&gt;</c>. Embedding the
/// parameters means a stored hash can always be verified even after the
/// configured cost is raised, and lets <see cref="NeedsRehash"/> detect
/// credentials worth upgrading.
/// </para>
/// </summary>
public sealed class Argon2PasswordHasher(IOptions<Argon2Options> options) : IPasswordHasher
{
    private const string Prefix = "$argon2id$";
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Version = 19;

    private readonly Argon2Options _options = options.Value;

    public string AlgorithmId => "argon2id";

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Derive(password, salt, _options.MemoryKib, _options.Iterations, _options.Parallelism);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Prefix}v={Version}$m={_options.MemoryKib},t={_options.Iterations},p={_options.Parallelism}$"
            + $"{Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");
    }

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash))
        {
            return false;
        }

        if (!TryParse(hash, out Argon2Parameters parameters, out byte[]? salt, out byte[]? expected))
        {
            // A malformed stored hash is a data problem, not a reason to grant
            // access. Fail closed.
            return false;
        }

        byte[] actual = Derive(
            password, salt, parameters.MemoryKib, parameters.Iterations, parameters.Parallelism);

        // Constant-time comparison. A byte-by-byte comparison that returns early
        // leaks how much of the hash matched, through timing.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public bool NeedsRehash(string hash)
    {
        if (!TryParse(hash, out Argon2Parameters parameters, out _, out _))
        {
            // Unparseable, so it cannot have been produced by the current
            // configuration. Upgrade it when the password is next known.
            return true;
        }

        // Only weaker-than-current warrants a rehash. A hash created with
        // stronger parameters is left alone rather than downgraded.
        return parameters.MemoryKib < _options.MemoryKib
            || parameters.Iterations < _options.Iterations
            || parameters.Parallelism < _options.Parallelism;
    }

    private static byte[] Derive(string password, byte[] salt, int memoryKib, int iterations, int parallelism)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKib,
            Iterations = iterations,
            DegreeOfParallelism = parallelism
        };

        return argon2.GetBytes(HashSize);
    }

    private static bool TryParse(
        string encoded,
        out Argon2Parameters parameters,
        out byte[] salt,
        out byte[] hash)
    {
        parameters = default;
        salt = [];
        hash = [];

        if (!encoded.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        // $argon2id$v=19$m=65536,t=3,p=2$<salt>$<hash>
        string[] parts = encoded.Split('$', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 5)
        {
            return false;
        }

        string[] costParts = parts[2].Split(',');

        if (costParts.Length != 3)
        {
            return false;
        }

        if (!TryReadInt(costParts[0], "m=", out int memoryKib)
            || !TryReadInt(costParts[1], "t=", out int iterations)
            || !TryReadInt(costParts[2], "p=", out int parallelism))
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[3]);
            hash = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        parameters = new Argon2Parameters(memoryKib, iterations, parallelism);

        return true;
    }

    private static bool TryReadInt(string segment, string prefix, out int value)
    {
        value = 0;

        return segment.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(segment.AsSpan(prefix.Length), CultureInfo.InvariantCulture, out value);
    }

    private readonly record struct Argon2Parameters(int MemoryKib, int Iterations, int Parallelism);
}
