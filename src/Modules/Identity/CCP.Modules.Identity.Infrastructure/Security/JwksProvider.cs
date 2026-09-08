using System.Security.Cryptography;
using CCP.Modules.Identity.Application.Abstractions;

namespace CCP.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Builds the JWKS document from the signing key's <b>public</b> parameters.
/// <para>
/// <see cref="ISigningKeyProvider.GetPublicKeyParameters"/> exports without
/// private parameters, and <see cref="JsonWebKeyDto"/> has no field that could
/// hold one. The private key therefore cannot reach this endpoint even if
/// someone later edits this method carelessly — the type system prevents it
/// rather than a comment asking nicely.
/// </para>
/// </summary>
public sealed class JwksProvider(ISigningKeyProvider signingKeyProvider) : IJwksProvider
{
    public JsonWebKeySet GetKeySet()
    {
        RSAParameters parameters = signingKeyProvider.GetPublicKeyParameters();

        if (parameters.Modulus is null || parameters.Exponent is null)
        {
            throw new InvalidOperationException(
                "The signing key did not export a public modulus and exponent.");
        }

        var key = new JsonWebKeyDto(
            Kid: signingKeyProvider.KeyId,
            Kty: "RSA",
            Use: "sig",
            Alg: "RS256",
            N: Base64UrlEncode(parameters.Modulus),
            E: Base64UrlEncode(parameters.Exponent));

        return new JsonWebKeySet([key]);
    }

    /// <summary>
    /// Base64url without padding, as RFC 7517 requires. Plain base64 would be
    /// rejected by conforming clients.
    /// </summary>
    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
