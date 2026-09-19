using CCP.Kernel.Results;

namespace CCP.Modules.Identity.Application.Abstractions;

/// <summary>What the browser returns when a passkey is created.</summary>
/// <param name="ClientDataJson">Base64url. What the browser says it was asked to do.</param>
/// <param name="AttestationObject">Base64url CBOR, carrying the new public key.</param>
public sealed record PasskeyRegistrationEvidence(
    string ClientDataJson,
    string AttestationObject);

/// <summary>A registration that verified, reduced to what is worth storing.</summary>
public sealed record VerifiedRegistration(
    string CredentialId,
    byte[] PublicKey,
    int Algorithm,
    uint SignCount,
    Guid? AuthenticatorGuid,
    bool UserVerified);

/// <summary>What the browser returns when a passkey is used to sign in.</summary>
public sealed record PasskeyAssertionEvidence(
    string CredentialId,
    string ClientDataJson,
    string AuthenticatorData,
    string Signature,

    /// <summary>
    /// Who the authenticator says this credential belongs to, set by this
    /// Platform when the passkey was created. Present for a discoverable
    /// credential, which is the only kind this Platform registers.
    /// </summary>
    string? UserHandle);

/// <summary>An assertion whose signature verified.</summary>
public sealed record VerifiedAssertion(uint SignCount, bool UserVerified, Guid? UserHandle);

/// <summary>
/// Checks what an authenticator sends back.
/// <para>
/// <b>Everything a passkey is worth rests on this interface.</b> The browser is
/// not trusted: it is a program on a machine the Platform does not control,
/// relaying a message from a device it has never seen. What makes the message
/// meaningful is that it is signed over a challenge this Platform chose, for
/// this Platform's domain, by a key this Platform already holds the public half
/// of.
/// </para>
/// <para>
/// An implementation that skips any one of those makes the whole thing
/// decorative, and it will pass every happy-path test while doing so. The tests
/// for this interface are therefore mostly about refusals.
/// </para>
/// </summary>
public interface IWebAuthnVerifier
{
    /// <summary>
    /// Checks a newly created passkey, and returns what should be stored.
    /// </summary>
    /// <param name="expectedChallenge">
    /// The challenge this Platform issued, base64url. Not the one the browser
    /// sent back: comparing the client's value with itself is the commonest way
    /// to build a passkey login that verifies nothing at all.
    /// </param>
    Result<VerifiedRegistration> VerifyRegistration(
        PasskeyRegistrationEvidence evidence,
        string expectedChallenge);

    /// <summary>
    /// Checks a signature made by a passkey already registered.
    /// </summary>
    /// <param name="publicKey">The COSE key stored at registration.</param>
    /// <param name="algorithm">The COSE algorithm that key was registered for.</param>
    Result<VerifiedAssertion> VerifyAssertion(
        PasskeyAssertionEvidence evidence,
        string expectedChallenge,
        byte[] publicKey,
        int algorithm);
}
