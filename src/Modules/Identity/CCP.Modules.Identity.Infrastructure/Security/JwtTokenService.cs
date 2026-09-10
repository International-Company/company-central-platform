using System.Security.Claims;
using CCP.Kernel.Application.Security;
using System.Security.Cryptography;
using CCP.Modules.Identity.Application;
using CCP.Modules.Identity.Application.Abstractions;
using CCP.Modules.Identity.Contracts;
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
public sealed class JwtTokenService : ITokenService, IPlatformTokenMinter, IDisposable
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

    public string CreateAccessToken(
        Guid userId,
        string username,
        Guid sessionId,
        DateTimeOffset now,
        bool mustChangePassword = false)
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

        if (mustChangePassword)
        {
            // Present only when true. An absent claim means no obligation, which
            // is what every token minted before this existed says -- so shipping
            // the check does not lock out everybody holding an older token.
            descriptor.Claims[PlatformClaims.MustChangePassword] = "true";
        }

        return _handler.CreateToken(descriptor);
    }

    /// <summary>
    /// A token for a registered application, signed by the same key as everybody
    /// else's.
    /// <para>
    /// <b>The subject depends on what the application is doing.</b> Acting as
    /// itself, the subject is the application; acting for somebody, the subject
    /// is that person and the application travels alongside. A resource server
    /// reading the token can therefore answer "who is this?" the same way it
    /// always has, and answer "who asked?" as well.
    /// </para>
    /// <para>
    /// No session claim. A machine token belongs to no session, has no refresh
    /// token, and is not revocable once issued — which is exactly why its
    /// lifetime is the same short one as everybody else's. Revoking a credential
    /// stops new tokens immediately and lets outstanding ones expire, and the
    /// documentation says so rather than implying revocation is instantaneous
    /// all the way down.
    /// </para>
    /// </summary>
    public string MintMachineToken(MachineTokenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        bool delegated = request.OnBehalfOfUserId is not null;

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] =
                (request.OnBehalfOfUserId ?? request.ApplicationId).ToString(),
            [JwtRegisteredClaimNames.Jti] = Guid.CreateVersion7().ToString(),

            // Read by the kernel to decide whether the subject is a person. A
            // machine token whose subject went unmarked would be treated as a
            // user whose id happens to be the application's.
            ["sub_type"] = delegated ? "user" : "application",

            ["app_id"] = request.ApplicationId.ToString(),
            ["app"] = request.ApplicationCode,

            // The credential used, so a leaked token points at one key to
            // revoke rather than at an application with several.
            ["client_id"] = request.ClientId
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = request.IssuedAt.UtcDateTime,
            NotBefore = request.IssuedAt.UtcDateTime,
            Expires = request.IssuedAt.Add(_options.AccessTokenLifetime).UtcDateTime,
            SigningCredentials = _signingCredentials,
            Claims = claims
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
