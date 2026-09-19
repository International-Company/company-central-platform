using System.Buffers.Text;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text.Json;
using CCP.Kernel.Results;
using CCP.Modules.Identity.Application;
using CCP.Modules.Identity.Application.Abstractions;
using CCP.Modules.Identity.Domain.Users;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Identity.Infrastructure.Security;

/// <summary>
/// The WebAuthn checks, written out one at a time.
/// <para>
/// This is the whole of what a passkey means, so every step below is a step
/// that, left out, leaves a sign-in that appears to work and proves nothing:
/// </para>
/// <list type="number">
/// <item><b>The challenge</b> is the one this Platform issued. Without it a
/// recorded exchange replays for ever.</item>
/// <item><b>The origin</b> is one of ours. This is what stops a copy of the
/// sign-in page on another domain from collecting usable assertions.</item>
/// <item><b>The relying party hash</b> in the authenticator's own data is the
/// hash of our domain. The origin above is the browser's word for it; this is
/// the authenticator's, and the two are checked separately because they are
/// asserted by different parties.</item>
/// <item><b>The type</b> is the ceremony we asked for. A signature collected
/// during registration must not be usable as a sign-in.</item>
/// <item><b>User presence and user verification</b> are both set. Presence
/// means somebody touched the device; verification means the device checked
/// who they were.</item>
/// <item><b>The signature</b> verifies against the stored public key, over the
/// authenticator's data and the hash of the client's data together — which is
/// what binds the two halves into one statement.</item>
/// </list>
/// <para>
/// <b>Written here rather than taken from a library</b>, because the subset a
/// platform authenticator needs is small and every line of it is a rule worth
/// being able to read. Attestation is not verified and not asked for: it says
/// which model of device this is, an enterprise wanting to allow only certain
/// hardware would need it, and this Platform does not — asking for it would
/// collect an identifier that lets devices be told apart, for a question nobody
/// here is asking.
/// </para>
/// </summary>
public sealed class WebAuthnVerifier(IOptions<IdentityOptions> options) : IWebAuthnVerifier
{
    private readonly WebAuthnOptions _options = options.Value.WebAuthn;

    /// <summary>The flag bits of the authenticator data's first byte.</summary>
    private const byte UserPresent = 0x01;

    private const byte UserVerified = 0x04;

    private const byte AttestedCredentialData = 0x40;

    /// <summary>ECDSA over P-256 with SHA-256. What a phone or a laptop produces.</summary>
    private const int Es256 = -7;

    /// <summary>RSASSA-PKCS1-v1_5 with SHA-256. What some security keys produce.</summary>
    private const int Rs256 = -257;

    public Result<VerifiedRegistration> VerifyRegistration(
        PasskeyRegistrationEvidence evidence,
        string expectedChallenge)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        try
        {
            byte[] clientDataBytes = Decode(evidence.ClientDataJson);

            Result clientData = CheckClientData(clientDataBytes, expectedChallenge, "webauthn.create");

            if (clientData.IsFailure)
            {
                return Result.Failure<VerifiedRegistration>(clientData.Error);
            }

            byte[] authenticatorData = ReadAuthenticatorDataFromAttestation(Decode(evidence.AttestationObject));

            Result flags = CheckAuthenticatorData(authenticatorData, requireAttestedCredential: true);

            if (flags.IsFailure)
            {
                return Result.Failure<VerifiedRegistration>(flags.Error);
            }

            return ReadAttestedCredential(authenticatorData);
        }
        catch (Exception exception) when (IsMalformedInput(exception))
        {
            // Anything that did not parse is a response this Platform will not
            // act on. Caught rather than thrown so a malformed body is a
            // refusal and not a 500, and so the exception text — which can
            // quote the input — never reaches the caller.
            return Result.Failure<VerifiedRegistration>(IdentityErrors.InvalidPasskeyRegistration);
        }
    }

    public Result<VerifiedAssertion> VerifyAssertion(
        PasskeyAssertionEvidence evidence,
        string expectedChallenge,
        byte[] publicKey,
        int algorithm)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(publicKey);

        try
        {
            byte[] clientDataBytes = Decode(evidence.ClientDataJson);

            Result clientData = CheckClientData(clientDataBytes, expectedChallenge, "webauthn.get");

            if (clientData.IsFailure)
            {
                return Result.Failure<VerifiedAssertion>(IdentityErrors.InvalidPasskeyAssertion);
            }

            byte[] authenticatorData = Decode(evidence.AuthenticatorData);

            // No attested credential data in an assertion: the credential
            // already exists, and an authenticator that sent one would be
            // answering a question nobody asked.
            Result flags = CheckAuthenticatorData(authenticatorData, requireAttestedCredential: false);

            if (flags.IsFailure)
            {
                return Result.Failure<VerifiedAssertion>(
                    flags.Error.Code == IdentityErrors.PasskeyUserVerificationRequired.Code
                        ? IdentityErrors.PasskeyUserVerificationRequired
                        : IdentityErrors.InvalidPasskeyAssertion);
            }

            // The two halves, joined. The authenticator signs its own data and
            // the hash of what the browser told it, so neither can be swapped
            // for a piece of another exchange.
            byte[] signedData = [.. authenticatorData, .. SHA256.HashData(clientDataBytes)];

            if (!VerifySignature(signedData, Decode(evidence.Signature), publicKey, algorithm))
            {
                return Result.Failure<VerifiedAssertion>(IdentityErrors.InvalidPasskeyAssertion);
            }

            return Result.Success(new VerifiedAssertion(
                SignCount: ReadSignCount(authenticatorData),
                UserVerified: (authenticatorData[32] & UserVerified) != 0,
                UserHandle: ReadUserHandle(evidence.UserHandle)));
        }
        catch (Exception exception) when (IsMalformedInput(exception))
        {
            return Result.Failure<VerifiedAssertion>(IdentityErrors.InvalidPasskeyAssertion);
        }
    }

    // -----------------------------------------------------------------------
    // Client data: the browser's account of what it was asked to do.
    // -----------------------------------------------------------------------

    private Result CheckClientData(byte[] clientDataBytes, string expectedChallenge, string expectedType)
    {
        using JsonDocument document = JsonDocument.Parse(clientDataBytes);

        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("type", out JsonElement type)
            || !string.Equals(type.GetString(), expectedType, StringComparison.Ordinal))
        {
            return Result.Failure(IdentityErrors.InvalidPasskeyRegistration);
        }

        if (!root.TryGetProperty("challenge", out JsonElement challenge))
        {
            return Result.Failure(IdentityErrors.InvalidPasskeyRegistration);
        }

        // Compared as bytes rather than as text: base64url has more than one
        // spelling of the same value once padding and case are considered, and
        // a string comparison would refuse a legitimate authenticator while
        // accepting nothing extra.
        //
        // Fixed-time, because a comparison that returns as soon as two bytes
        // differ tells an attacker how much of a guess was right.
        if (!CryptographicOperations.FixedTimeEquals(
                Decode(challenge.GetString() ?? string.Empty),
                Decode(expectedChallenge)))
        {
            return Result.Failure(IdentityErrors.InvalidPasskeyRegistration);
        }

        if (!root.TryGetProperty("origin", out JsonElement origin) || !IsAllowedOrigin(origin.GetString()))
        {
            return Result.Failure(IdentityErrors.InvalidPasskeyRegistration);
        }

        // A ceremony run inside a frame on another site. Refused: the person
        // cannot see whose page they are answering for.
        if (root.TryGetProperty("crossOrigin", out JsonElement crossOrigin)
            && crossOrigin.ValueKind == JsonValueKind.True)
        {
            return Result.Failure(IdentityErrors.InvalidPasskeyRegistration);
        }

        return Result.Success();
    }

    private bool IsAllowedOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin)
            || !Uri.TryCreate(origin, UriKind.Absolute, out Uri? parsed))
        {
            return false;
        }

        // Compared part by part rather than as a string. Scheme and host are
        // case-insensitive and the port may be written or implied, so two
        // spellings of one origin are common and neither should be refused;
        // everything else must match exactly.
        foreach (string allowed in _options.Origins)
        {
            if (Uri.TryCreate(allowed, UriKind.Absolute, out Uri? candidate)
                && string.Equals(parsed.Scheme, candidate.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(parsed.Host, candidate.Host, StringComparison.OrdinalIgnoreCase)
                && parsed.Port == candidate.Port)
            {
                return true;
            }
        }

        return false;
    }

    // -----------------------------------------------------------------------
    // Authenticator data: the device's own statement, which the browser relays
    // but cannot alter without breaking the signature over it.
    // -----------------------------------------------------------------------

    private Result CheckAuthenticatorData(byte[] authenticatorData, bool requireAttestedCredential)
    {
        // 32 bytes of relying-party hash, one of flags, four of counter.
        if (authenticatorData.Length < 37)
        {
            return Result.Failure(IdentityErrors.InvalidPasskeyRegistration);
        }

        // The domain the authenticator believes it is answering for. The origin
        // check above is the browser's claim; this one is the device's, and a
        // browser that lied about the first cannot forge the second.
        byte[] expectedRpIdHash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_options.RelyingPartyId));

        if (!CryptographicOperations.FixedTimeEquals(authenticatorData.AsSpan(0, 32), expectedRpIdHash))
        {
            return Result.Failure(IdentityErrors.InvalidPasskeyRegistration);
        }

        byte flags = authenticatorData[32];

        if ((flags & UserPresent) == 0)
        {
            return Result.Failure(IdentityErrors.InvalidPasskeyRegistration);
        }

        // The check that makes a passkey worth two factors. Refused rather than
        // downgraded: see IdentityErrors.PasskeyUserVerificationRequired.
        if ((flags & UserVerified) == 0)
        {
            return Result.Failure(IdentityErrors.PasskeyUserVerificationRequired);
        }

        if (requireAttestedCredential && (flags & AttestedCredentialData) == 0)
        {
            return Result.Failure(IdentityErrors.InvalidPasskeyRegistration);
        }

        return Result.Success();
    }

    private static uint ReadSignCount(byte[] authenticatorData)
        => System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(authenticatorData.AsSpan(33, 4));

    /// <summary>
    /// Pulls the authenticator data out of the CBOR attestation object,
    /// ignoring the attestation statement this Platform does not ask for.
    /// </summary>
    private static byte[] ReadAuthenticatorDataFromAttestation(byte[] attestationObject)
    {
        var reader = new CborReader(attestationObject, CborConformanceMode.Strict);

        int? count = reader.ReadStartMap();
        byte[]? authenticatorData = null;

        for (int i = 0; count is null || i < count; i++)
        {
            if (count is null && reader.PeekState() == CborReaderState.EndMap)
            {
                break;
            }

            string key = reader.ReadTextString();

            if (key == "authData")
            {
                authenticatorData = reader.ReadByteString();
            }
            else
            {
                reader.SkipValue();
            }
        }

        reader.ReadEndMap();

        return authenticatorData ?? throw new CborContentException("No authenticator data.");
    }

    /// <summary>
    /// Reads the credential and its public key out of the attested credential
    /// data that follows the fixed header.
    /// </summary>
    private static Result<VerifiedRegistration> ReadAttestedCredential(byte[] authenticatorData)
    {
        // 16 bytes of authenticator model, 2 of credential length.
        if (authenticatorData.Length < 55)
        {
            return Result.Failure<VerifiedRegistration>(IdentityErrors.InvalidPasskeyRegistration);
        }

        var authenticatorGuid = new Guid(authenticatorData.AsSpan(37, 16), bigEndian: true);

        int credentialIdLength =
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(authenticatorData.AsSpan(53, 2));

        if (credentialIdLength is 0 or > 1023 || authenticatorData.Length < 55 + credentialIdLength)
        {
            return Result.Failure<VerifiedRegistration>(IdentityErrors.InvalidPasskeyRegistration);
        }

        byte[] credentialId = authenticatorData.AsSpan(55, credentialIdLength).ToArray();
        byte[] coseKey = authenticatorData.AsSpan(55 + credentialIdLength).ToArray();

        // Parsed *and built* now rather than at the first sign-in. A key this
        // Platform cannot use is a passkey that would be registered, listed as
        // a way to sign in, and fail every time it was tried.
        //
        // The first version only read the algorithm here, and a test found what
        // that left open: a key whose coordinates are not a point on the curve
        // parses as CBOR perfectly well, is stored, and then throws on every
        // sign-in attempt for ever. Building it here makes "we stored it" and
        // "we can verify with it" the same statement.
        int? algorithm = ReadAlgorithm(coseKey);

        if (algorithm is not (Es256 or Rs256) || !CanVerifyWith(coseKey, algorithm.Value))
        {
            return Result.Failure<VerifiedRegistration>(IdentityErrors.InvalidPasskeyRegistration);
        }

        return Result.Success(new VerifiedRegistration(
            CredentialId: Base64Url.EncodeToString(credentialId),
            PublicKey: coseKey,
            Algorithm: algorithm.Value,
            SignCount: ReadSignCount(authenticatorData),

            // All zeroes means the device declined to identify its model, which
            // is its right and the common case for a phone.
            AuthenticatorGuid: authenticatorGuid == Guid.Empty ? null : authenticatorGuid,
            UserVerified: (authenticatorData[32] & UserVerified) != 0));
    }

    // -----------------------------------------------------------------------
    // The key itself.
    // -----------------------------------------------------------------------

    private static bool VerifySignature(byte[] signedData, byte[] signature, byte[] coseKey, int algorithm)
        => algorithm switch
        {
            Es256 => VerifyEs256(signedData, signature, coseKey),
            Rs256 => VerifyRs256(signedData, signature, coseKey),

            // An algorithm this Platform never registers. Reached only if a row
            // were written by something other than the registration path.
            _ => false,
        };

    /// <summary>
    /// Whether a key can be built at all. Used at registration, so nothing is
    /// stored that cannot later be used.
    /// </summary>
    private static bool CanVerifyWith(byte[] coseKey, int algorithm)
    {
        switch (algorithm)
        {
            case Es256:
                using (ECDsa? ecdsa = CreateEs256(coseKey))
                {
                    return ecdsa is not null;
                }

            case Rs256:
                using (RSA? rsa = CreateRs256(coseKey))
                {
                    return rsa is not null;
                }

            default:
                return false;
        }
    }

    private static bool VerifyEs256(byte[] signedData, byte[] signature, byte[] coseKey)
    {
        using ECDsa? ecdsa = CreateEs256(coseKey);

        // WebAuthn signatures are ASN.1 DER, not the fixed-width pair .NET
        // defaults to. Verifying with the wrong format fails every genuine
        // signature, which looks exactly like a wrong key.
        return ecdsa is not null
            && ecdsa.VerifyData(
                signedData, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    }

    private static bool VerifyRs256(byte[] signedData, byte[] signature, byte[] coseKey)
    {
        using RSA? rsa = CreateRs256(coseKey);

        return rsa is not null
            && rsa.VerifyData(signedData, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    private static ECDsa? CreateEs256(byte[] coseKey)
    {
        CoseKey key = ReadCoseKey(coseKey);

        if (key.X is null || key.Y is null)
        {
            return null;
        }

        try
        {
            return ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = key.X, Y = key.Y },
            });
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (PlatformNotSupportedException)
        {
            // What Windows raises for coordinates that are not a point on the
            // curve: CNG reports "the parameter is incorrect" and .NET wraps it
            // as though the platform lacked P-256 entirely. Caught here, beside
            // the import that causes it, rather than added to the list of
            // malformed-input exceptions — where it would also swallow a real
            // platform that genuinely cannot do this, which is a deployment
            // problem and should be reported as one.
            return null;
        }
    }

    private static RSA? CreateRs256(byte[] coseKey)
    {
        CoseKey key = ReadCoseKey(coseKey);

        if (key.Modulus is null || key.Exponent is null)
        {
            return null;
        }

        try
        {
            return RSA.Create(new RSAParameters { Modulus = key.Modulus, Exponent = key.Exponent });
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private sealed record CoseKey(int? Algorithm, byte[]? X, byte[]? Y, byte[]? Modulus, byte[]? Exponent);

    private static int? ReadAlgorithm(byte[] coseKey) => ReadCoseKey(coseKey).Algorithm;

    /// <summary>
    /// Reads the COSE key map. Labels are small integers: 3 is the algorithm,
    /// -1 and -2 are the curve point for an elliptic key or the modulus and
    /// exponent for an RSA one.
    /// </summary>
    private static CoseKey ReadCoseKey(byte[] coseKey)
    {
        var reader = new CborReader(coseKey, CborConformanceMode.Strict);

        int? count = reader.ReadStartMap();

        int? algorithm = null;
        int? keyType = null;
        byte[]? first = null;
        byte[]? second = null;

        for (int i = 0; count is null || i < count; i++)
        {
            if (count is null && reader.PeekState() == CborReaderState.EndMap)
            {
                break;
            }

            int label = reader.ReadInt32();

            switch (label)
            {
                case 1:
                    keyType = reader.ReadInt32();
                    break;
                case 3:
                    algorithm = reader.ReadInt32();
                    break;
                case -1:
                    // The curve for an elliptic key, a byte string for RSA.
                    if (reader.PeekState() is CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger)
                    {
                        int curve = reader.ReadInt32();

                        // Only P-256, which is the curve ES256 names. Another
                        // curve with the ES256 label is a key that would verify
                        // against the wrong parameters.
                        if (curve != 1)
                        {
                            return new CoseKey(null, null, null, null, null);
                        }
                    }
                    else
                    {
                        first = reader.ReadByteString();
                    }

                    break;
                case -2:
                    if (keyType == 2)
                    {
                        first = reader.ReadByteString();
                    }
                    else
                    {
                        second = reader.ReadByteString();
                    }

                    break;
                case -3:
                    second = reader.ReadByteString();
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }

        reader.ReadEndMap();

        return keyType switch
        {
            2 => new CoseKey(algorithm, first, second, null, null),
            3 => new CoseKey(algorithm, null, null, first, second),
            _ => new CoseKey(null, null, null, null, null),
        };
    }

    // -----------------------------------------------------------------------

    private static Guid? ReadUserHandle(string? userHandle)
    {
        if (string.IsNullOrWhiteSpace(userHandle))
        {
            return null;
        }

        byte[] bytes = Decode(userHandle);

        return bytes.Length == 16 ? new Guid(bytes, bigEndian: true) : null;
    }

    private static byte[] Decode(string base64Url) => Base64Url.DecodeFromChars(base64Url);

    /// <summary>
    /// Whether this is the response being malformed rather than the Platform
    /// being broken. Anything else is left to propagate: a bug here should be
    /// reported as one, not returned as "your passkey is invalid".
    /// </summary>
    private static bool IsMalformedInput(Exception exception)
        => exception is FormatException
            or JsonException
            or CborContentException
            or ArgumentException
            or IndexOutOfRangeException
            or CryptographicException;
}
