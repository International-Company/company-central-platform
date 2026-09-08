using System.Security.Cryptography;
using System.Text;
using CCP.Modules.Security.Application.Abstractions;

namespace CCP.Modules.Security.Infrastructure.Protection;

/// <summary>
/// Generates and hashes recovery codes.
/// <para>
/// A recovery code is a full authentication bypass for the second factor, so it
/// is treated like any other credential: cryptographically random, stored
/// hashed, shown once, single use.
/// </para>
/// <para>
/// <b>SHA-256 rather than Argon2id.</b> The same reasoning as refresh tokens: the
/// input is high-entropy random rather than a human-chosen secret, so there is no
/// low-entropy guess space for an attacker to search and the memory-hard hashing
/// passwords need would only add latency. Argon2id protects against guessing what
/// a person would choose; nobody guesses 50 bits of randomness.
/// </para>
/// </summary>
public sealed class RecoveryCodeGenerator : IRecoveryCodeGenerator
{
    /// <summary>
    /// Characters used. Deliberately excludes the pairs people confuse when
    /// reading a printed code aloud or typing it back: 0/O, 1/I/L, 2/Z, 5/S,
    /// 8/B. A code that is correct but unreadable is a support call.
    /// </summary>
    private const string Alphabet = "ACDEFGHJKMNPQRTUVWXY34679";

    /// <summary>
    /// Characters per code. Ten from a 25-character alphabet is about 46 bits —
    /// far beyond guessing, and still short enough to write down.
    /// </summary>
    private const int CodeLength = 10;

    /// <summary>Where the hyphen goes, purely so the code is readable.</summary>
    private const int GroupSize = 5;

    public (IReadOnlyList<string> Codes, IReadOnlyList<string> Hashes) Generate(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 50);

        var codes = new List<string>(count);
        var hashes = new List<string>(count);

        for (int i = 0; i < count; i++)
        {
            string code = GenerateCode();

            codes.Add(code);
            hashes.Add(Hash(code));
        }

        return (codes, hashes);
    }

    public string Hash(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        // Normalised before hashing, so a user typing lower case or omitting the
        // hyphen still matches. Without this, a correct code fails because of
        // formatting the user was never told mattered.
        string normalized = Normalize(code);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    /// <summary>
    /// Strips formatting and upper-cases, so presentation differences cannot
    /// cause a false rejection.
    /// </summary>
    internal static string Normalize(string code)
    {
        var builder = new StringBuilder(code.Length);

        foreach (char c in code)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(char.ToUpperInvariant(c));
            }
        }

        return builder.ToString();
    }

    private static string GenerateCode()
    {
        var builder = new StringBuilder(CodeLength + 1);

        for (int i = 0; i < CodeLength; i++)
        {
            if (i > 0 && i % GroupSize == 0)
            {
                builder.Append('-');
            }

            // RandomNumberGenerator.GetItems draws uniformly without the modulo
            // bias a naive index computation would introduce.
            builder.Append(RandomNumberGenerator.GetItems<char>(Alphabet, 1)[0]);
        }

        return builder.ToString();
    }
}
