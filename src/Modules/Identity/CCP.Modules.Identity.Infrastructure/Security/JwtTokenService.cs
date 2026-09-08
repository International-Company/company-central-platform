using System.Security.Claims;
using System.Security.Cryptography;
using CCP.Modules.Identity.Application;
using CCP.Modules.Identity.Application.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CCP.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Issues and validates RS256-signed access tokens (ADR-006 §13.3).
/// <para>
/// Asymmetric signing, not HMAC, and that choice matters: business applications
/// must be able to validate a Platform token locally against the published JWKS.
/// With a shared symmetric secret, every consumer would need the key that
/// <i>mints</i> tokens — so any one of them could forge them — or every request
/// in the company would have to call the Platform to validate, making it a
/// synchronous bottleneck for every system.
/// </para>
/// <para>
/// Claims are kept minimal: subject, session, username, issuer, audience,
/// lifetime. Permissions are deliberately absent — the Authorization module
/// (Phase 4) decides how those travel (ADR-007 §14.4).
/// </para>
/// </summary>
public sealed class JwtTokenService : ITokenService, IDisposable
{
    private readonly IdentityOptions _options;
    private readonly RSA _rsa;
    private readonly SigningCredentials _signingCredentials;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtTokenService(IOptions<IdentityOptions> options, ISigningKeyProvider keyProvider)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);

        _options = options.Value;
        _rsa = keyProvider.GetSigningKey();

        var securityKey = new RsaSecurityKey(_rsa) { KeyId = keyProvider.KeyId };

        _signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);
    }

    public TimeSpan AccessTokenLifetime => _options.AccessTokenLifetime;

    public string CreateAccessToken(Guid userId, string username, Guid sessionId, DateTimeOffset now)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.Add(_options.AccessTokenLifetime).UtcDateTime,
            SigningCredentials = _signingCredentials,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = userId.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.CreateVersion7().ToString(),
                ["username"] = username,

                // The session this token belongs to. Lets a resource server tie
                // a token back to revocable server-side state.
                ["sid"] = sessionId.ToString()
            }
        };

        return _handler.CreateToken(descriptor);
    }

    public ClaimsPrincipal? ValidateAccessToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var parameters = new TokenValidationParameters
        {
            ValidIssuer = _options.Issuer,
            ValidAudience = _options.Audience,
            IssuerSigningKey = new RsaSecurityKey(_rsa),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            // No clock skew allowance. The default is five minutes, which keeps
            // an expired token working for five minutes past its expiry — and
            // the expiry is the only revocation an access token has.
            ClockSkew = TimeSpan.Zero
        };

        TokenValidationResult result = _handler.ValidateTokenAsync(token, parameters)
            .GetAwaiter()
            .GetResult();

        return result.IsValid ? new ClaimsPrincipal(result.ClaimsIdentity) : null;
    }

    public void Dispose() => _rsa.Dispose();
}
