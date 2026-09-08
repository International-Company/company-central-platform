using System.Text;
using CCP.Modules.Security.Domain.Mfa;

namespace CCP.Modules.Security.UnitTests.Domain;

/// <summary>
/// TOTP, verified against the RFC 6238 published test vectors.
/// <para>
/// A hand-written cryptographic routine that is only tested against itself is
/// worthless: it would pass its own tests while rejecting every real
/// authenticator app. The vectors are the only evidence that this implementation
/// agrees with the rest of the world.
/// </para>
/// </summary>
public sealed class TotpTests
{
    /// <summary>
    /// The RFC 6238 Appendix B secret for SHA-1: the ASCII string
    /// "12345678901234567890".
    /// </summary>
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    /// <summary>
    /// RFC 6238 Appendix B, SHA-1 rows. Times are Unix seconds; the expected
    /// values are the last six digits of the published eight-digit codes,
    /// because this implementation uses the six-digit form every authenticator
    /// app expects.
    /// </summary>
    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void MatchesTheRfc6238TestVectors(long unixSeconds, string expected)
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

        Assert.Equal(expected, Totp.ComputeCode(RfcSecret, at));
    }

    [Fact]
    public void AFreshlyComputedCodeVerifies()
    {
        byte[] secret = Totp.GenerateSecret();
        var now = DateTimeOffset.UtcNow;

        Assert.True(Totp.Verify(secret, Totp.ComputeCode(secret, now), now));
    }

    [Fact]
    public void ACodeFromAnotherSecretIsRejected()
    {
        byte[] mine = Totp.GenerateSecret();
        byte[] theirs = Totp.GenerateSecret();
        var now = DateTimeOffset.UtcNow;

        Assert.False(Totp.Verify(mine, Totp.ComputeCode(theirs, now), now));
    }

    // -----------------------------------------------------------------------
    // The drift window
    // -----------------------------------------------------------------------

    [Fact]
    public void ACodeFromThePreviousPeriodIsAccepted()
    {
        // Phone clocks drift and people type slowly. Zero tolerance produces
        // constant spurious failures.
        byte[] secret = Totp.GenerateSecret();
        var now = DateTimeOffset.UtcNow;

        string previous = Totp.ComputeCode(secret, now.AddSeconds(-Totp.PeriodSeconds));

        Assert.True(Totp.Verify(secret, previous, now));
    }

    [Fact]
    public void ACodeFromTheNextPeriodIsAccepted()
    {
        byte[] secret = Totp.GenerateSecret();
        var now = DateTimeOffset.UtcNow;

        string next = Totp.ComputeCode(secret, now.AddSeconds(Totp.PeriodSeconds));

        Assert.True(Totp.Verify(secret, next, now));
    }

    [Fact]
    public void ACodeOutsideTheWindowIsRejected()
    {
        // The window is also the attacker's guessing surface: every accepted
        // period multiplies the codes valid at any instant. Two periods away
        // must not work.
        byte[] secret = Totp.GenerateSecret();
        var now = DateTimeOffset.UtcNow;

        string tooOld = Totp.ComputeCode(secret, now.AddSeconds(-Totp.PeriodSeconds * 3));
        string tooNew = Totp.ComputeCode(secret, now.AddSeconds(Totp.PeriodSeconds * 3));

        Assert.False(Totp.Verify(secret, tooOld, now));
        Assert.False(Totp.Verify(secret, tooNew, now));
    }

    // -----------------------------------------------------------------------
    // Malformed input
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    public void MalformedInputIsRejectedWithoutThrowing(string? code)
    {
        byte[] secret = Totp.GenerateSecret();

        Assert.False(Totp.Verify(secret, code, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SurroundingWhitespaceIsTolerated()
    {
        // Users paste codes. Rejecting a correct code because it arrived with a
        // space would be a support call, not a security control.
        byte[] secret = Totp.GenerateSecret();
        var now = DateTimeOffset.UtcNow;

        Assert.True(Totp.Verify(secret, $"  {Totp.ComputeCode(secret, now)}  ", now));
    }

    // -----------------------------------------------------------------------
    // Secrets and provisioning
    // -----------------------------------------------------------------------

    [Fact]
    public void GeneratedSecretsAreTheRecommendedLengthAndDistinct()
    {
        byte[] first = Totp.GenerateSecret();
        byte[] second = Totp.GenerateSecret();

        Assert.Equal(Totp.SecretSizeBytes, first.Length);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void CodesAreAlwaysSixDigits()
    {
        // Including when the computed value is small. Without zero-padding, a
        // code of 42 would be sent as "42" and never match.
        byte[] secret = Totp.GenerateSecret();
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        for (int i = 0; i < 500; i++)
        {
            string code = Totp.ComputeCode(secret, start.AddSeconds(i * Totp.PeriodSeconds));

            Assert.Equal(Totp.Digits, code.Length);
            Assert.All(code, c => Assert.True(char.IsAsciiDigit(c)));
        }
    }

    [Fact]
    public void Base32EncodingMatchesRfc4648()
    {
        // Authenticator apps require base32, not base64. Getting this wrong
        // produces a QR code that scans and then never works.
        Assert.Equal("MZXW6===".TrimEnd('='), Totp.Base32Encode(Encoding.ASCII.GetBytes("foo")));
        Assert.Equal("MFRGG===".TrimEnd('='), Totp.Base32Encode(Encoding.ASCII.GetBytes("abc")));
        Assert.Equal("MZXW6YTBOI======".TrimEnd('='),
            Totp.Base32Encode(Encoding.ASCII.GetBytes("foobar")));
    }

    [Fact]
    public void TheProvisioningUriIsWellFormed()
    {
        byte[] secret = Totp.GenerateSecret();

        string uri = Totp.BuildProvisioningUri("Company Platform", "ahmad@example.com", secret);

        Assert.StartsWith("otpauth://totp/", uri, StringComparison.Ordinal);
        Assert.Contains("secret=", uri, StringComparison.Ordinal);
        Assert.Contains("digits=6", uri, StringComparison.Ordinal);
        Assert.Contains("period=30", uri, StringComparison.Ordinal);

        // Spaces and the @ must be escaped, or the URI breaks in a QR reader.
        Assert.DoesNotContain(" ", uri, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProvisioningUriCarriesTheSameSecretThatVerifies()
    {
        // The whole enrolment flow depends on this: the QR code and the stored
        // secret must be the same secret, or every code the user produces will
        // be rejected.
        byte[] secret = Totp.GenerateSecret();

        string uri = Totp.BuildProvisioningUri("Platform", "ahmad", secret);

        Assert.Contains($"secret={Totp.Base32Encode(secret)}", uri, StringComparison.Ordinal);
    }
}
