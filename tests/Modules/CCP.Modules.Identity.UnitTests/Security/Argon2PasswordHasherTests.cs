using System.Diagnostics;
using CCP.Modules.Identity.Application.Abstractions;
using CCP.Modules.Identity.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Identity.UnitTests.Security;

/// <summary>
/// Password hashing is the control that decides whether a stolen database is a
/// catastrophe or an inconvenience, so its properties are pinned here rather
/// than assumed.
/// </summary>
public sealed class Argon2PasswordHasherTests
{
    // Reduced cost so the suite stays fast. The production parameters are
    // exercised separately in ProductionParameters_AreWithinAReasonableTimeBudget.
    private static readonly Argon2Options TestOptions = new()
    {
        MemoryKib = 8192,
        Iterations = 1,
        Parallelism = 1
    };

    private static IPasswordHasher CreateHasher(Argon2Options? options = null)
        => new Argon2PasswordHasher(Options.Create(options ?? TestOptions));

    [Fact]
    public void Hash_ThenVerify_Succeeds()
    {
        IPasswordHasher hasher = CreateHasher();

        string hash = hasher.Hash("correct horse battery staple");

        Assert.True(hasher.Verify("correct horse battery staple", hash));
    }

    [Fact]
    public void Verify_RejectsAWrongPassword()
    {
        IPasswordHasher hasher = CreateHasher();

        string hash = hasher.Hash("correct horse battery staple");

        Assert.False(hasher.Verify("Correct horse battery staple", hash));
        Assert.False(hasher.Verify("correct horse battery stapl", hash));
        Assert.False(hasher.Verify(string.Empty, hash));
    }

    [Fact]
    public void Hash_NeverContainsThePlaintext()
    {
        // The obvious failure, worth an explicit assertion because it is
        // catastrophic and silent if it ever regresses.
        IPasswordHasher hasher = CreateHasher();

        const string password = "a-very-distinctive-password-value";

        string hash = hasher.Hash(password);

        Assert.DoesNotContain(password, hash, StringComparison.Ordinal);
    }

    [Fact]
    public void Hash_ProducesADifferentValueEachTime()
    {
        // Distinct salts. Identical hashes for identical passwords would let an
        // attacker see which users share one, and make rainbow tables viable.
        IPasswordHasher hasher = CreateHasher();

        string first = hasher.Hash("same password");
        string second = hasher.Hash("same password");

        Assert.NotEqual(first, second);
        Assert.True(hasher.Verify("same password", first));
        Assert.True(hasher.Verify("same password", second));
    }

    [Fact]
    public void Hash_IsSelfDescribing()
    {
        // Parameters are embedded, so a hash stays verifiable after the
        // configured cost is raised.
        IPasswordHasher hasher = CreateHasher();

        string hash = hasher.Hash("password value");

        Assert.StartsWith("$argon2id$", hash, StringComparison.Ordinal);
        Assert.Contains("m=8192", hash, StringComparison.Ordinal);
        Assert.Contains("t=1", hash, StringComparison.Ordinal);
        Assert.Contains("p=1", hash, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("$argon2id$")]
    [InlineData("$argon2id$v=19$m=bad,t=3,p=2$c2FsdA==$aGFzaA==")]
    [InlineData("$argon2id$v=19$m=8192,t=1,p=1$!!!not-base64!!!$aGFzaA==")]
    [InlineData("$bcrypt$v=19$m=8192,t=1,p=1$c2FsdA==$aGFzaA==")]
    public void Verify_FailsClosedOnAMalformedHash(string malformed)
    {
        // A corrupt stored hash must deny access, never grant it. Failing open
        // here would turn a data problem into an authentication bypass.
        IPasswordHasher hasher = CreateHasher();

        Assert.False(hasher.Verify("any password", malformed));
    }

    [Fact]
    public void VerifyOldHash_StillSucceedsAfterCostIsRaised()
    {
        // The migration path: a credential hashed with yesterday's parameters
        // must keep working after the cost is increased, or raising the cost
        // would lock every existing user out.
        IPasswordHasher weak = CreateHasher(new Argon2Options
        {
            MemoryKib = 8192,
            Iterations = 1,
            Parallelism = 1
        });

        string oldHash = weak.Hash("unchanged password");

        IPasswordHasher strong = CreateHasher(new Argon2Options
        {
            MemoryKib = 16384,
            Iterations = 2,
            Parallelism = 1
        });

        Assert.True(strong.Verify("unchanged password", oldHash));
    }

    [Fact]
    public void NeedsRehash_IsTrueForWeakerParameters()
    {
        IPasswordHasher weak = CreateHasher(new Argon2Options
        {
            MemoryKib = 8192,
            Iterations = 1,
            Parallelism = 1
        });

        string oldHash = weak.Hash("password value");

        IPasswordHasher strong = CreateHasher(new Argon2Options
        {
            MemoryKib = 16384,
            Iterations = 2,
            Parallelism = 1
        });

        Assert.True(strong.NeedsRehash(oldHash));
    }

    [Fact]
    public void NeedsRehash_IsFalseForCurrentParameters()
    {
        IPasswordHasher hasher = CreateHasher();

        Assert.False(hasher.NeedsRehash(hasher.Hash("password value")));
    }

    [Fact]
    public void NeedsRehash_IsFalseForStrongerParameters()
    {
        // A hash created with stronger parameters is left alone rather than
        // downgraded to the current configuration.
        IPasswordHasher strong = CreateHasher(new Argon2Options
        {
            MemoryKib = 32768,
            Iterations = 3,
            Parallelism = 2
        });

        string strongHash = strong.Hash("password value");

        IPasswordHasher weaker = CreateHasher(new Argon2Options
        {
            MemoryKib = 8192,
            Iterations = 1,
            Parallelism = 1
        });

        Assert.False(weaker.NeedsRehash(strongHash));
    }

    [Fact]
    public void NeedsRehash_IsTrueForAnUnparseableHash()
    {
        IPasswordHasher hasher = CreateHasher();

        Assert.True(hasher.NeedsRehash("legacy-hash-from-another-system"));
    }

    [Fact]
    public void Hash_HandlesUnicodeAndVeryLongPasswords()
    {
        // Arabic passwords must work, and a long passphrase must not throw
        // (ADR-011: Arabic is a first-class language here).
        IPasswordHasher hasher = CreateHasher();

        const string arabic = "كلمة-مرور-طويلة-جدا-باللغة-العربية";
        string longPassword = new('x', 256);

        Assert.True(hasher.Verify(arabic, hasher.Hash(arabic)));
        Assert.True(hasher.Verify(longPassword, hasher.Hash(longPassword)));
    }

    [Fact]
    public void ProductionParameters_AreWithinAReasonableTimeBudget()
    {
        // The default cost targets roughly 100 ms. This asserts only a generous
        // upper bound: too slow and an authentication burst becomes a
        // self-inflicted denial of service. It is a smoke check, not a
        // benchmark — Phase 5 measures the real figures on real hardware.
        IPasswordHasher hasher = CreateHasher(new Argon2Options());

        var stopwatch = Stopwatch.StartNew();
        string hash = hasher.Hash("a representative password");
        stopwatch.Stop();

        Assert.True(hasher.Verify("a representative password", hash));

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(3),
            $"Hashing with the default parameters took {stopwatch.ElapsedMilliseconds} ms, which is "
            + "far above the ~100 ms target. Re-tune Argon2Options against this hardware.");
    }
}
