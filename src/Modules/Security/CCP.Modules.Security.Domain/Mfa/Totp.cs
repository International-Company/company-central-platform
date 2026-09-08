using System.Security.Cryptography;
using System.Text;

namespace CCP.Modules.Security.Domain.Mfa;

/// <summary>
/// Time-based one-time passwords (RFC 6238).
/// <para>
/// TOTP first, because it needs no provider, costs nothing, and works offline
/// (ARCHITECTURE.md §12.5). SMS is neither free nor trustworthy — SIM swapping
/// is a routine attack — and WebAuthn, though better, needs hardware the company
/// does not yet have. Both remain extension points.
/// </para>
/// <para>
/// The algorithm: HMAC the counter with the shared secret, take four bytes at a
/// dynamically chosen offset, and reduce them modulo 10^digits. It is small, and
/// it is written out here rather than pulled from a package because the whole
/// thing is thirty lines and a dependency in the authentication path is a
/// dependency in the authentication path.
/// </para>
/// </summary>
public static class Totp
{
    /// <summary>Digits in a code. Six, as every authenticator app expects.</summary>
    public const int Digits = 6;

    /// <summary>Seconds a code is valid for. Thirty, per RFC 6238.</summary>
    public const int PeriodSeconds = 30;

    /// <summary>
    /// How many periods either side of now are accepted.
    /// <para>
    /// One period — thirty seconds each way. Phone clocks drift and people type
    /// slowly, so zero tolerance produces constant spurious failures. But the
    /// window is also the attacker's guessing surface: each accepted period
    /// triples the codes that work at any instant, so it stays at one.
    /// </para>
    /// </summary>
    public const int ToleranceperiodS = 1;

    /// <summary>Bytes of secret. 160 bits, the RFC 4226 recommendation.</summary>
    public const int SecretSizeBytes = 20;

    private static readonly long UnixEpochTicks =
        new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).Ticks;

    /// <summary>Generates a new shared secret.</summary>
    public static byte[] GenerateSecret() => RandomNumberGenerator.GetBytes(SecretSizeBytes);

    /// <summary>
    /// Computes the code for a given instant. Used by tests and by enrolment
    /// verification; a client normally computes its own.
    /// </summary>
    public static string ComputeCode(byte[] secret, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(secret);

        return ComputeForCounter(secret, CounterFor(at));
    }

    /// <summary>
    /// Verifies a code against the secret, allowing for clock drift.
    /// <para>
    /// The comparison is constant-time. A code is a six-digit secret and an
    /// early-exit comparison leaks, digit by digit, how much of a guess was
    /// right — which reduces the search from a million to about sixty.
    /// </para>
    /// </summary>
    public static bool Verify(byte[] secret, string? code, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(secret);

        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        string candidate = code.Trim();

        if (candidate.Length != Digits)
        {
            return false;
        }

        long counter = CounterFor(at);
        bool matched = false;

        // Every period in the window is checked, and the loop does not break on
        // a match. Returning early would make a code from the current period
        // verify measurably faster than one from the previous, which leaks
        // timing information about which period matched.
        for (int offset = -ToleranceperiodS; offset <= ToleranceperiodS; offset++)
        {
            string expected = ComputeForCounter(secret, counter + offset);

            if (FixedTimeEquals(expected, candidate))
            {
                matched = true;
            }
        }

        return matched;
    }

    /// <summary>
    /// The <c>otpauth://</c> URI an authenticator app scans.
    /// </summary>
    /// <param name="issuer">Shown in the app, so the user knows which account.</param>
    /// <param name="accountName">Normally the username or email.</param>
    /// <param name="secret">The shared secret.</param>
    public static string BuildProvisioningUri(string issuer, string accountName, byte[] secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentNullException.ThrowIfNull(secret);

        string escapedIssuer = Uri.EscapeDataString(issuer);
        string escapedAccount = Uri.EscapeDataString(accountName);

        return $"otpauth://totp/{escapedIssuer}:{escapedAccount}"
             + $"?secret={Base32Encode(secret)}"
             + $"&issuer={escapedIssuer}"
             + $"&algorithm=SHA1&digits={Digits}&period={PeriodSeconds}";
    }

    /// <summary>
    /// The secret in the form a user types when their camera will not scan the
    /// QR code. Same secret, base32-encoded.
    /// </summary>
    public static string Base32Secret(byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        return Base32Encode(secret);
    }

    /// <summary>
    /// Base32, as authenticator apps require for the secret. Not base64 —
    /// the otpauth specification predates it here and every client expects
    /// base32.
    /// </summary>
    internal static string Base32Encode(byte[] data)
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        var builder = new StringBuilder((data.Length * 8 + 4) / 5);

        int buffer = 0;
        int bitsLeft = 0;

        foreach (byte b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;

            while (bitsLeft >= 5)
            {
                builder.Append(Alphabet[(buffer >> (bitsLeft - 5)) & 31]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0)
        {
            builder.Append(Alphabet[(buffer << (5 - bitsLeft)) & 31]);
        }

        return builder.ToString();
    }

    private static long CounterFor(DateTimeOffset at)
        => (at.UtcTicks - UnixEpochTicks) / TimeSpan.TicksPerSecond / PeriodSeconds;

    private static string ComputeForCounter(byte[] secret, long counter)
    {
        Span<byte> counterBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);

        Span<byte> hash = stackalloc byte[20];

        // HMAC-SHA1 is what RFC 6238 specifies and what every authenticator app
        // implements — Google Authenticator supports nothing else. Choosing
        // SHA-256 here would produce codes no user could generate.
        //
        // The analyser is right to flag SHA-1 in general and wrong to flag it
        // here: the collision attacks that broke SHA-1 do not apply to HMAC,
        // which depends on the compression function's PRF property rather than
        // collision resistance. HMAC-SHA1 remains sound, and NIST still permits
        // it for exactly this reason. Suppressed locally, not globally, because
        // SHA-1 anywhere else in this codebase would be a real problem.
#pragma warning disable CA5350
        HMACSHA1.HashData(secret, counterBytes, hash);
#pragma warning restore CA5350

        // Dynamic truncation (RFC 4226 §5.4): the low nibble of the last byte
        // selects where in the hash to read from, so the same secret and counter
        // do not always use the same bytes.
        int offset = hash[^1] & 0x0F;

        int binary = ((hash[offset] & 0x7F) << 24)
                   | ((hash[offset + 1] & 0xFF) << 16)
                   | ((hash[offset + 2] & 0xFF) << 8)
                   | (hash[offset + 3] & 0xFF);

        int otp = binary % (int)Math.Pow(10, Digits);

        return otp.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(Digits, '0');
    }

    private static bool FixedTimeEquals(string left, string right)
        => left.Length == right.Length
        && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
}
