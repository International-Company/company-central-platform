using System.Security.Cryptography;
using CCP.Modules.Security.Infrastructure.Protection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Security.UnitTests.Infrastructure;

/// <summary>
/// Replacing the key that protects every enrolled second factor.
/// <para>
/// <b>It could not be done at all.</b> A stored secret was bare base64 of
/// <c>nonce | tag | ciphertext</c> with nothing to say which key wrote it, so a
/// new key made every existing enrolment undecryptable — which means locking
/// every user out of their own account. A key that can never be replaced is a
/// key that stays in place after the laptop it was generated on is sold.
/// </para>
/// <para>
/// The assertion that matters most here is the dullest: that secrets written
/// before any of this existed still read. A rotation story that required a
/// flag-day re-encryption of every row would not be a rotation story.
/// </para>
/// </summary>
public sealed class KeyRotationTests : IDisposable
{
    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (string file in _files)
        {
            File.Delete(file);
        }
    }

    /// <summary>
    /// The case that makes this deployable: a value written by the old code,
    /// which named no version and no key, still reads.
    /// </summary>
    [Fact]
    public void ASecretStoredBeforeVersioningStillReads()
    {
        string keyPath = AKeyFile(out byte[] key);
        byte[] secret = RandomNumberGenerator.GetBytes(20);

        // Exactly what the previous implementation wrote: no prefix, no key
        // name, just the bytes.
        string legacy = LegacyProtect(key, secret);

        using MfaSecretProtector protector = Build(keyPath, "2");

        Assert.Equal(secret, protector.Unprotect(legacy));
    }

    [Fact]
    public void WhatIsWrittenNowNamesItsKey()
    {
        using MfaSecretProtector protector = Build(AKeyFile(out _), "2026-09");

        string stored = protector.Protect(RandomNumberGenerator.GetBytes(20));

        Assert.StartsWith("v2.2026-09.", stored, StringComparison.Ordinal);
    }

    /// <summary>
    /// The rotation itself: a secret written under the old key is still
    /// readable once that key has been retired and a new one is active.
    /// </summary>
    [Fact]
    public void ASecretWrittenUnderARetiredKeyStillReads()
    {
        string oldPath = AKeyFile(out _);
        string newPath = AKeyFile(out _);

        byte[] secret = RandomNumberGenerator.GetBytes(20);

        string stored;

        using (MfaSecretProtector before = Build(oldPath, "1"))
        {
            stored = before.Protect(secret);
        }

        using MfaSecretProtector after = Build(
            newPath, "2", retired: [new RetiredMfaKey { Id = "1", KeyPath = oldPath }]);

        Assert.Equal(secret, after.Unprotect(stored));
    }

    /// <summary>
    /// And a new secret goes under the new key, so the old one stops
    /// accumulating work for itself.
    /// </summary>
    [Fact]
    public void AfterARotationNewSecretsUseTheNewKey()
    {
        string oldPath = AKeyFile(out _);
        string newPath = AKeyFile(out byte[] newKey);

        using MfaSecretProtector after = Build(
            newPath, "2", retired: [new RetiredMfaKey { Id = "1", KeyPath = oldPath }]);

        byte[] secret = RandomNumberGenerator.GetBytes(20);
        string stored = after.Protect(secret);

        Assert.StartsWith("v2.2.", stored, StringComparison.Ordinal);

        // Decrypted with the new key directly, so this is not merely the
        // protector agreeing with itself about a label.
        Assert.Equal(secret, LegacyUnprotect(newKey, stored.Split('.', 3)[2]));
    }

    /// <summary>
    /// A secret naming a key this deployment does not hold is denied rather
    /// than guessed at. Trying every key would turn a configuration mistake into
    /// a silent success under the wrong assumption.
    /// </summary>
    [Fact]
    public void ASecretNamingAnUnknownKeyIsDenied()
    {
        string path = AKeyFile(out _);

        string stored;

        using (MfaSecretProtector other = Build(path, "9"))
        {
            stored = other.Protect(RandomNumberGenerator.GetBytes(20));
        }

        using MfaSecretProtector current = Build(path, "1");

        Assert.Null(current.Unprotect(stored));
    }

    /// <summary>
    /// A full stop separates the fields of the stored form, so a key id
    /// containing one would write values that cannot be read back. Refused at
    /// startup rather than discovered on the day of a rotation.
    /// </summary>
    [Fact]
    public void AKeyIdContainingASeparatorIsRefused()
    {
        string path = AKeyFile(out _);

        Assert.Throws<InvalidOperationException>(() => Build(path, "2026.09"));
    }

    /// <summary>
    /// The same key listed as both active and retired is a mistake, and which
    /// of the two was meant cannot be guessed. Refusing to start says so.
    /// </summary>
    [Fact]
    public void AKeyThatIsBothActiveAndRetiredIsRefused()
    {
        string path = AKeyFile(out _);

        Assert.Throws<InvalidOperationException>(
            () => Build(path, "1", retired: [new RetiredMfaKey { Id = "1", KeyPath = path }]));
    }

    [Fact]
    public void ARetiredKeyWithNoIdIsRefused()
    {
        string path = AKeyFile(out _);

        Assert.Throws<InvalidOperationException>(
            () => Build(path, "2", retired: [new RetiredMfaKey { KeyPath = path }]));
    }

    // --- Fixtures -----------------------------------------------------------

    private static MfaSecretProtector Build(
        string keyPath, string activeKeyId, RetiredMfaKey[]? retired = null)
    {
        var options = new MfaProtectionOptions
        {
            KeyPath = keyPath,
            ActiveKeyId = activeKeyId,
        };

        foreach (RetiredMfaKey key in retired ?? [])
        {
            options.RetiredKeys.Add(key);
        }

        return new MfaSecretProtector(
            Options.Create(options),
            new ProductionEnvironment(),
            NullLogger<MfaSecretProtector>.Instance);
    }

    private string AKeyFile(out byte[] key)
    {
        key = RandomNumberGenerator.GetBytes(32);

        string path = Path.Combine(Path.GetTempPath(), $"ccp-mfa-{Guid.NewGuid():N}.key");

        File.WriteAllText(path, Convert.ToBase64String(key));
        _files.Add(path);

        return path;
    }

    /// <summary>What the implementation wrote before versioning existed.</summary>
    private static string LegacyProtect(byte[] key, byte[] secret)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] ciphertext = new byte[secret.Length];
        byte[] tag = new byte[16];

        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, secret, ciphertext, tag);

        byte[] combined = new byte[12 + 16 + ciphertext.Length];
        nonce.CopyTo(combined, 0);
        tag.CopyTo(combined, 12);
        ciphertext.CopyTo(combined, 12 + 16);

        return Convert.ToBase64String(combined);
    }

    /// <summary>The same, in reverse, so a test can check the bytes itself.</summary>
    private static byte[] LegacyUnprotect(byte[] key, string payload)
    {
        byte[] combined = Convert.FromBase64String(payload);

        byte[] nonce = combined[..12];
        byte[] tag = combined[12..28];
        byte[] ciphertext = combined[28..];
        byte[] plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "CCP.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
