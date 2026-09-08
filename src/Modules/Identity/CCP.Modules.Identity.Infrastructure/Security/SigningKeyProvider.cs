using System.Security.Cryptography;
using CCP.Modules.Identity.Application;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Identity.Infrastructure.Security;

/// <summary>Supplies the RSA key used to sign access tokens.</summary>
public interface ISigningKeyProvider
{
    /// <summary>Identifies the key, published in JWKS as <c>kid</c> so consumers can select it during rotation.</summary>
    string KeyId { get; }

    /// <summary>A new RSA instance holding the key. The caller owns and disposes it.</summary>
    RSA GetSigningKey();

    /// <summary>The public key, for the JWKS endpoint.</summary>
    RSAParameters GetPublicKeyParameters();
}

/// <summary>
/// Loads the token signing key from a PEM file whose path comes from
/// configuration.
/// <para>
/// <b>The key material never appears in configuration, in the repository, or in
/// any committed file</b> (ARCHITECTURE.md §12.7). Configuration carries a
/// <i>path</i>; the file comes from the cloud secret manager, a mounted secret,
/// or a developer's own machine.
/// </para>
/// <para>
/// In Development only, and only when no path is configured, an ephemeral key is
/// generated in memory so a developer can run the Platform immediately after
/// cloning. That key exists only for the life of the process: restarting
/// invalidates every token, which is exactly the behaviour that makes it
/// unusable in production by accident. Outside Development a missing key is a
/// fatal startup error, deliberately — silently inventing a signing key in
/// production would mean tokens that survive no deployment and a security
/// property nobody can reason about.
/// </para>
/// </summary>
public sealed class FileSigningKeyProvider : ISigningKeyProvider
{
    private readonly byte[] _privateKeyBytes;

    public FileSigningKeyProvider(
        IOptions<IdentityOptions> options,
        IHostEnvironment environment,
        ILogger<FileSigningKeyProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(environment);

        string? path = options.Value.SigningKeyPath;

        if (!string.IsNullOrWhiteSpace(path) && !path.StartsWith("REPLACE_WITH", StringComparison.Ordinal))
        {
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"The token signing key was not found at '{path}'. "
                    + "Set Identity:SigningKeyPath to a readable PEM file containing an RSA private key.");
            }

            using RSA loaded = RSA.Create();
            loaded.ImportFromPem(File.ReadAllText(path));

            if (loaded.KeySize < 2048)
            {
                throw new InvalidOperationException(
                    $"The token signing key at '{path}' is {loaded.KeySize} bits. "
                    + "RSA keys used for token signing must be at least 2048 bits.");
            }

            _privateKeyBytes = loaded.ExportRSAPrivateKey();
            KeyId = ComputeKeyId(loaded);

            return;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Identity:SigningKeyPath is not configured. A token signing key must be supplied "
                + "explicitly outside Development; the Platform will not generate one, because a "
                + "generated key would change on every deployment and invalidate every token.");
        }

        using RSA generated = RSA.Create(3072);
        _privateKeyBytes = generated.ExportRSAPrivateKey();
        KeyId = ComputeKeyId(generated);

        logger.LogWarning(
            "No token signing key configured. An ephemeral development key has been generated "
            + "(KeyId={KeyId}). All tokens become invalid when this process restarts. "
            + "This is permitted in Development only.",
            KeyId);
    }

    public string KeyId { get; }

    public RSA GetSigningKey()
    {
        RSA rsa = RSA.Create();
        rsa.ImportRSAPrivateKey(_privateKeyBytes, out _);

        return rsa;
    }

    public RSAParameters GetPublicKeyParameters()
    {
        using RSA rsa = GetSigningKey();

        return rsa.ExportParameters(includePrivateParameters: false);
    }

    /// <summary>
    /// A stable id derived from the public key, so the same key always yields
    /// the same <c>kid</c> and consumers can cache JWKS across restarts.
    /// Derived from the public part only — a key id must never be a function of
    /// the private key.
    /// </summary>
    private static string ComputeKeyId(RSA rsa)
    {
        byte[] publicKey = rsa.ExportRSAPublicKey();
        byte[] digest = SHA256.HashData(publicKey);

        return Convert.ToHexStringLower(digest.AsSpan(0, 8));
    }
}
