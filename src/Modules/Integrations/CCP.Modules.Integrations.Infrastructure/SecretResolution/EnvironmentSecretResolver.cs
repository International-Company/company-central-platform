using System.Text;
using CCP.Modules.Integrations.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CCP.Modules.Integrations.Infrastructure.SecretResolution;

/// <summary>
/// Resolves a secret reference from the environment.
/// <para>
/// <b>The folder is called SecretResolution and not Secrets on purpose.</b> The
/// repository ignores <c>**/secrets/</c>, which is a rule worth keeping strict —
/// it is there to stop anybody committing a folder of credentials. It silently
/// swallowed this file on the first push, which is the rule working: a source
/// directory named <c>Secrets</c> is exactly the sort of thing it should be
/// suspicious of. Renaming the folder was the correct resolution; weakening the
/// rule was not.
/// </para>
/// <para>
/// <b>The implementation a deployment gets when it has not chosen a secret
/// manager</b>, and it is a real one rather than a placeholder. A reference like
/// <c>integrations/acme-bank/api-key</c> is read from
/// <c>CCP_SECRETS__INTEGRATIONS__ACME_BANK__API_KEY</c>, which on every cloud
/// platform is set the same way as every other environment variable and is
/// exactly as protected.
/// </para>
/// <para>
/// The property that matters holds either way: <b>the database never contains a
/// credential</b>. Moving to AWS Secrets Manager or Azure Key Vault later is one
/// implementation of this interface and no change to any provider row — which is
/// the point of naming secrets rather than storing them.
/// </para>
/// <para>
/// The resolved value is not cached. A secret rotated in the environment takes
/// effect on the next call rather than on the next restart, and the cost is a
/// dictionary lookup.
/// </para>
/// </summary>
public sealed class EnvironmentSecretResolver(
    IConfiguration configuration,
    ILogger<EnvironmentSecretResolver> logger) : ISecretResolver
{
    /// <summary>
    /// The configuration section secrets live under, so that a reference cannot
    /// be pointed at an arbitrary setting.
    /// <para>
    /// Without the prefix, a reference of <c>ConnectionStrings:Platform</c>
    /// would resolve — and a provider row would then be a way to read the
    /// database password out of the Platform's own configuration.
    /// </para>
    /// </summary>
    private const string SecretsSection = "Secrets";

    public Task<string?> ResolveAsync(
        string reference, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        string key = $"{SecretsSection}:{Normalise(reference)}";
        string? value = configuration[key];

        if (string.IsNullOrEmpty(value) && logger.IsEnabled(LogLevel.Warning))
        {
            // The reference is logged; the value never is, because there is none
            // and because there would be no reason to log one if there were.
            logger.LogWarning(
                "The secret reference {Reference} resolved to nothing. The provider is "
                + "configured and its credential is not available.",
                reference);
        }

        return Task.FromResult(string.IsNullOrEmpty(value) ? null : value);
    }

    /// <summary>
    /// Turns a reference into a configuration key.
    /// <para>
    /// Slashes become the configuration separator and hyphens become
    /// underscores, so <c>integrations/acme-bank/api-key</c> becomes
    /// <c>Secrets:INTEGRATIONS:ACME_BANK:API_KEY</c> — which the environment
    /// provider reads from
    /// <c>CCP_Secrets__INTEGRATIONS__ACME_BANK__API_KEY</c>.
    /// </para>
    /// <para>
    /// Anything that is not a letter, a digit or a separator is dropped rather
    /// than passed through. A reference is administrator-supplied text, and
    /// letting it contain arbitrary characters would make the shape of the key
    /// it produces harder to reason about than it is worth.
    /// </para>
    /// </summary>
    private static string Normalise(string reference)
    {
        var key = new StringBuilder(reference.Length);

        foreach (char character in reference.Trim())
        {
            if (character is '/' or ':')
            {
                key.Append(':');
            }
            else if (character is '-' or '.' or ' ')
            {
                key.Append('_');
            }
            else if (char.IsLetterOrDigit(character) || character == '_')
            {
                key.Append(char.ToUpperInvariant(character));
            }
        }

        return key.ToString();
    }
}
