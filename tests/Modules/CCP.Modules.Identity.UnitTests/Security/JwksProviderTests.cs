using System.Security.Cryptography;
using System.Text.Json;
using CCP.Modules.Identity.Application.Abstractions;
using CCP.Modules.Identity.Infrastructure.Security;

namespace CCP.Modules.Identity.UnitTests.Security;

/// <summary>
/// JWKS is a public endpoint that exposes key material, so the one thing that
/// must never happen — a private component escaping — is asserted directly.
/// </summary>
public sealed class JwksProviderTests
{
    private static JwksProvider CreateProvider(out TestSigningKeyProvider keyProvider)
    {
        keyProvider = new TestSigningKeyProvider();

        return new JwksProvider(keyProvider);
    }

    [Fact]
    public void PublishesOneRsaSigningKey()
    {
        JwksProvider provider = CreateProvider(out TestSigningKeyProvider keyProvider);

        JsonWebKeySet keySet = provider.GetKeySet();

        JsonWebKeyDto key = Assert.Single(keySet.Keys);

        Assert.Equal(keyProvider.KeyId, key.Kid);
        Assert.Equal("RSA", key.Kty);
        Assert.Equal("sig", key.Use);
        Assert.Equal("RS256", key.Alg);
        Assert.False(string.IsNullOrWhiteSpace(key.N));
        Assert.False(string.IsNullOrWhiteSpace(key.E));
    }

    [Fact]
    public void NeverExposesAPrivateComponent()
    {
        // The decisive test. If a private parameter ever reached this document,
        // every consumer could mint Platform identities. The DTO has no field
        // that could carry one, and this asserts the serialized form too — so a
        // future field added carelessly fails here.
        JwksProvider provider = CreateProvider(out _);

        string json = JsonSerializer.Serialize(provider.GetKeySet());

        // RFC 7517 names the RSA private components d, p, q, dp, dq, qi.
        foreach (string privateMember in new[] { "\"d\"", "\"p\"", "\"q\"", "\"dp\"", "\"dq\"", "\"qi\"" })
        {
            Assert.DoesNotContain(privateMember, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void PublishedKeyMatchesTheSigningKey()
    {
        // A JWKS that does not correspond to the signing key is worse than none:
        // every consumer would reject every valid token.
        JwksProvider provider = CreateProvider(out TestSigningKeyProvider keyProvider);

        JsonWebKeyDto published = provider.GetKeySet().Keys[0];

        RSAParameters expected = keyProvider.GetPublicKeyParameters();

        Assert.Equal(Base64Url(expected.Modulus!), published.N);
        Assert.Equal(Base64Url(expected.Exponent!), published.E);
    }

    [Fact]
    public void EncodingIsBase64UrlWithoutPadding()
    {
        // RFC 7517 requires base64url. Standard base64 would be rejected by
        // conforming clients, and '+' or '/' in a URL-safe field is a bug.
        JwksProvider provider = CreateProvider(out _);

        JsonWebKeyDto key = provider.GetKeySet().Keys[0];

        foreach (string value in new[] { key.N, key.E })
        {
            Assert.DoesNotContain("=", value, StringComparison.Ordinal);
            Assert.DoesNotContain("+", value, StringComparison.Ordinal);
            Assert.DoesNotContain("/", value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void KeyIdIsStableAcrossCalls()
    {
        // Consumers cache JWKS by kid. An id that changed per call would defeat
        // caching and break key selection during rotation.
        JwksProvider provider = CreateProvider(out _);

        Assert.Equal(provider.GetKeySet().Keys[0].Kid, provider.GetKeySet().Keys[0].Kid);
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
