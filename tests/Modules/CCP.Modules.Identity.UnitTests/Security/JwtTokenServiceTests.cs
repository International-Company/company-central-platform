using System.Security.Claims;
using System.Security.Cryptography;
using CCP.Modules.Identity.Application;
using CCP.Modules.Identity.Application.Abstractions;
using CCP.Modules.Identity.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Identity.UnitTests.Security;

/// <summary>
/// Access tokens are bearer credentials that resource servers trust without
/// asking the Platform, so the properties that make them trustworthy are
/// asserted rather than assumed (ADR-006).
/// </summary>
public sealed class JwtTokenServiceTests
{
    // Issued at the real current time, not a fixed past instant: validation
    // enforces lifetime with no clock-skew allowance, so a token minted at a
    // hard-coded timestamp is already expired by the time it is checked.
    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    private static readonly IdentityOptions Options = new()
    {
        Issuer = "https://platform.test",
        Audience = "ccp-api-test",
        AccessTokenLifetime = TimeSpan.FromMinutes(15)
    };

    private static JwtTokenService CreateService(IdentityOptions? options = null)
        => new(Microsoft.Extensions.Options.Options.Create(options ?? Options), new TestSigningKeyProvider());

    [Fact]
    public void IssuedToken_ValidatesAndCarriesItsClaims()
    {
        using JwtTokenService service = CreateService();

        Guid userId = Guid.CreateVersion7();
        Guid sessionId = Guid.CreateVersion7();

        string token = service.CreateAccessToken(userId, "ahmad", sessionId, Now);

        ClaimsPrincipal? principal = service.ValidateAccessToken(token);

        Assert.NotNull(principal);
        Assert.Equal(userId.ToString(), principal.FindFirst("sub")?.Value);
        Assert.Equal(sessionId.ToString(), principal.FindFirst("sid")?.Value);
        Assert.Equal("ahmad", principal.FindFirst("username")?.Value);
    }

    [Fact]
    public void Token_CarriesNoPermissionClaims()
    {
        // Permissions are deliberately absent in Phase 2. The Authorization
        // module (Phase 4) decides how they travel; embedding them early would
        // pre-empt ADR-007 §14.4.
        using JwtTokenService service = CreateService();

        string token = service.CreateAccessToken(Guid.CreateVersion7(), "ahmad", Guid.CreateVersion7(), Now);

        ClaimsPrincipal principal = service.ValidateAccessToken(token)!;

        Assert.DoesNotContain(principal.Claims, c =>
            c.Type.Contains("permission", StringComparison.OrdinalIgnoreCase)
            || c.Type.Contains("role", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TamperedToken_IsRejected()
    {
        // The signature is the entire basis on which a resource server trusts a
        // token it did not issue.
        using JwtTokenService service = CreateService();

        string token = service.CreateAccessToken(Guid.CreateVersion7(), "ahmad", Guid.CreateVersion7(), Now);

        string[] parts = token.Split('.');
        string tamperedPayload = parts[1][..^2] + (parts[1][^2] == 'A' ? "BB" : "AA");
        string tampered = $"{parts[0]}.{tamperedPayload}.{parts[2]}";

        Assert.Null(service.ValidateAccessToken(tampered));
    }

    [Fact]
    public void TokenSignedByAnotherKey_IsRejected()
    {
        // A token from a different signing key must not validate, or any holder
        // of any key could mint Platform identities.
        using JwtTokenService issuer = CreateService();
        using JwtTokenService other = CreateService();

        string token = issuer.CreateAccessToken(Guid.CreateVersion7(), "ahmad", Guid.CreateVersion7(), Now);

        Assert.Null(other.ValidateAccessToken(token));
    }

    [Fact]
    public void TokenWithTheWrongAudience_IsRejected()
    {
        using JwtTokenService service = CreateService();

        string token = service.CreateAccessToken(Guid.CreateVersion7(), "ahmad", Guid.CreateVersion7(), Now);

        using var differentAudience = new JwtTokenService(
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions
            {
                Issuer = Options.Issuer,
                Audience = "a-different-api",
                AccessTokenLifetime = Options.AccessTokenLifetime
            }),
            new TestSigningKeyProvider());

        Assert.Null(differentAudience.ValidateAccessToken(token));
    }

    [Fact]
    public void ExpiredToken_IsRejected()
    {
        // Issued far enough in the past that it is already expired. Expiry is
        // the only revocation an access token has, which is why the lifetime is
        // short and the clock skew allowance is zero.
        using JwtTokenService service = CreateService();

        string token = service.CreateAccessToken(
            Guid.CreateVersion7(), "ahmad", Guid.CreateVersion7(), DateTimeOffset.UtcNow.AddHours(-2));

        Assert.Null(service.ValidateAccessToken(token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-token")]
    [InlineData("a.b.c")]
    public void MalformedInput_IsRejectedWithoutThrowing(string input)
    {
        using JwtTokenService service = CreateService();

        Assert.Null(service.ValidateAccessToken(input));
    }

    [Fact]
    public void EachToken_HasAUniqueIdentifier()
    {
        // The jti is what makes a token individually identifiable, which any
        // future replay detection or denylist depends on.
        using JwtTokenService service = CreateService();

        Guid userId = Guid.CreateVersion7();
        Guid sessionId = Guid.CreateVersion7();

        string first = service.CreateAccessToken(userId, "ahmad", sessionId, Now);
        string second = service.CreateAccessToken(userId, "ahmad", sessionId, Now);

        string firstJti = service.ValidateAccessToken(first)!.FindFirst("jti")!.Value;
        string secondJti = service.ValidateAccessToken(second)!.FindFirst("jti")!.Value;

        Assert.NotEqual(firstJti, secondJti);
    }
}

/// <summary>An in-memory RSA key, so tests need no key file.</summary>
internal sealed class TestSigningKeyProvider : ISigningKeyProvider
{
    private readonly byte[] _privateKey;

    public TestSigningKeyProvider()
    {
        using RSA rsa = RSA.Create(2048);
        _privateKey = rsa.ExportRSAPrivateKey();
        KeyId = Convert.ToHexStringLower(SHA256.HashData(rsa.ExportRSAPublicKey()).AsSpan(0, 8));
    }

    public string KeyId { get; }

    public RSA GetSigningKey()
    {
        RSA rsa = RSA.Create();
        rsa.ImportRSAPrivateKey(_privateKey, out _);

        return rsa;
    }

    public RSAParameters GetPublicKeyParameters()
    {
        using RSA rsa = GetSigningKey();

        return rsa.ExportParameters(includePrivateParameters: false);
    }
}
