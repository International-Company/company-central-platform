namespace CCP.Modules.Identity.Application.Abstractions;

/// <summary>
/// Publishes the Platform's token-signing public keys as a JWKS document.
/// <para>
/// This is the mechanism ADR-006 relies on: a business application validates a
/// Platform token locally against these keys, rather than calling the Platform
/// on every request. Without it the Platform becomes a synchronous dependency of
/// every request in the company.
/// </para>
/// <para>
/// Declared as a port here so the Api layer can expose the endpoint without
/// referencing Infrastructure, where the key material lives (§6.1).
/// </para>
/// </summary>
public interface IJwksProvider
{
    /// <summary>
    /// The current key set. Public parameters only — a JWKS containing a private
    /// key would hand every consumer the ability to mint Platform identities.
    /// </summary>
    JsonWebKeySet GetKeySet();
}

/// <summary>A JWKS document (RFC 7517).</summary>
/// <param name="Keys">The published keys.</param>
public sealed record JsonWebKeySet(IReadOnlyList<JsonWebKeyDto> Keys);

/// <summary>
/// One RSA public key in JWKS form.
/// <para>
/// Only the modulus and exponent are present. There is no field here capable of
/// carrying a private component, which makes leaking one through this endpoint
/// impossible by construction rather than by care.
/// </para>
/// </summary>
/// <param name="Kid">Key id, so a consumer can select the right key during rotation.</param>
/// <param name="Kty">Key type — always <c>RSA</c>.</param>
/// <param name="Use">Intended use — always <c>sig</c>.</param>
/// <param name="Alg">Signing algorithm — always <c>RS256</c>.</param>
/// <param name="N">Base64url modulus.</param>
/// <param name="E">Base64url exponent.</param>
public sealed record JsonWebKeyDto(
    string Kid,
    string Kty,
    string Use,
    string Alg,
    string N,
    string E);
