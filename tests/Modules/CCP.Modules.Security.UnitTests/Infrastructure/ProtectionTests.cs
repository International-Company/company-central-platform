using System.Security.Cryptography;
using CCP.Modules.Security.Infrastructure.Protection;

namespace CCP.Modules.Security.UnitTests.Infrastructure;

/// <summary>Recovery code generation, hashing, and the normalisation around it.</summary>
public sealed class RecoveryCodeGeneratorTests
{
    private readonly RecoveryCodeGenerator _generator = new();

    [Fact]
    public void Generate_ReturnsAsManyCodesAsAsked()
    {
        (IReadOnlyList<string> codes, IReadOnlyList<string> hashes) = _generator.Generate(10);

        Assert.Equal(10, codes.Count);
        Assert.Equal(10, hashes.Count);
    }

    [Fact]
    public void Generate_ProducesDistinctCodes()
    {
        (IReadOnlyList<string> codes, _) = _generator.Generate(50);

        Assert.Equal(50, codes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Generate_HashesMatchTheCodesReturned()
    {
        (IReadOnlyList<string> codes, IReadOnlyList<string> hashes) = _generator.Generate(5);

        for (int i = 0; i < codes.Count; i++)
        {
            Assert.Equal(hashes[i], _generator.Hash(codes[i]));
        }
    }

    [Fact]
    public void Generate_NeverStoresThePlaintext()
    {
        (IReadOnlyList<string> codes, IReadOnlyList<string> hashes) = _generator.Generate(5);

        // A hash that contained its input would defeat the point of storing
        // hashes at all.
        foreach (string code in codes)
        {
            Assert.DoesNotContain(hashes, h => h.Contains(code, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Generate_AvoidsCharactersPeopleConfuse()
    {
        (IReadOnlyList<string> codes, _) = _generator.Generate(50);

        // 0/O, 1/I/L, 2/Z, 5/S and 8/B are the pairs that turn a correct code
        // into a support call.
        foreach (char c in string.Concat(codes).Replace("-", "", StringComparison.Ordinal))
        {
            Assert.DoesNotContain(c, "OIL01258BZS");
        }
    }

    [Theory]
    [InlineData("ACDEF-GHJKM", "acdef-ghjkm")]
    [InlineData("ACDEF-GHJKM", "ACDEFGHJKM")]
    [InlineData("ACDEF-GHJKM", "  acdef ghjkm  ")]
    public void Hash_IgnoresFormattingTheUserWasNeverToldMattered(string canonical, string variant)
        => Assert.Equal(_generator.Hash(canonical), _generator.Hash(variant));

    [Fact]
    public void Hash_DistinguishesDifferentCodes()
        => Assert.NotEqual(_generator.Hash("ACDEF-GHJKM"), _generator.Hash("ACDEF-GHJKN"));

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Generate_RefusesAnUnreasonableCount(int count)
        => Assert.Throws<ArgumentOutOfRangeException>(() => _generator.Generate(count));
}

/// <summary>
/// Encryption of TOTP secrets at rest.
/// <para>
/// The secret must be recoverable to verify a code, so unlike a password it
/// cannot be hashed. These tests check the two properties that make that
/// acceptable: the ciphertext is not the plaintext, and it cannot be altered
/// without detection.
/// </para>
/// </summary>
public sealed class MfaSecretProtectorTests : IDisposable
{
    private readonly string _keyPath = Path.Combine(Path.GetTempPath(), $"ccp-mfa-{Guid.NewGuid():N}.key");

    public MfaSecretProtectorTests()
        => File.WriteAllText(_keyPath, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

    public void Dispose()
    {
        if (File.Exists(_keyPath))
        {
            File.Delete(_keyPath);
        }
    }

    private MfaSecretProtector CreateProtector() => TestProtectorFactory.Create(_keyPath);

    [Fact]
    public void Protect_ThenUnprotect_ReturnsTheOriginalSecret()
    {
        MfaSecretProtector protector = CreateProtector();
        byte[] secret = RandomNumberGenerator.GetBytes(20);

        byte[]? roundTripped = protector.Unprotect(protector.Protect(secret));

        Assert.NotNull(roundTripped);
        Assert.Equal(secret, roundTripped);
    }

    [Fact]
    public void Protect_ProducesDifferentCiphertextEachTime()
    {
        MfaSecretProtector protector = CreateProtector();
        byte[] secret = RandomNumberGenerator.GetBytes(20);

        // A fresh nonce per encryption. Identical ciphertext for identical
        // secrets would reveal which users share one, and worse.
        Assert.NotEqual(protector.Protect(secret), protector.Protect(secret));
    }

    [Fact]
    public void Unprotect_ReturnsNullForTamperedCiphertext()
    {
        MfaSecretProtector protector = CreateProtector();
        byte[] secret = RandomNumberGenerator.GetBytes(20);

        byte[] raw = Convert.FromBase64String(protector.Protect(secret));
        raw[^1] ^= 0xFF;

        // AES-GCM authenticates. Null rather than a wrong secret is the whole
        // reason for choosing an AEAD mode here.
        Assert.Null(protector.Unprotect(Convert.ToBase64String(raw)));
    }

    [Fact]
    public void Unprotect_ReturnsNullForRubbish()
    {
        MfaSecretProtector protector = CreateProtector();

        Assert.Null(protector.Unprotect("not base64 at all"));
        Assert.Null(protector.Unprotect(Convert.ToBase64String([1, 2, 3])));
    }

    [Fact]
    public void Unprotect_ReturnsNullUnderADifferentKey()
    {
        byte[] secret = RandomNumberGenerator.GetBytes(20);
        string protectedSecret = CreateProtector().Protect(secret);

        string otherKeyPath = Path.Combine(Path.GetTempPath(), $"ccp-mfa-{Guid.NewGuid():N}.key");
        File.WriteAllText(otherKeyPath, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

        try
        {
            // What a leaked database backup gets you without the key: nothing.
            Assert.Null(TestProtectorFactory.Create(otherKeyPath).Unprotect(protectedSecret));
        }
        finally
        {
            File.Delete(otherKeyPath);
        }
    }
}
