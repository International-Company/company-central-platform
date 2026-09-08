using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CCP.Modules.Identity.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Identity.Infrastructure.Security;

/// <summary>Configuration for breached-password screening.</summary>
public sealed class BreachedPasswordOptions
{
    public const string SectionName = "Identity:BreachedPasswords";

    /// <summary>
    /// Whether to query the external range API. Off by default: enabling an
    /// outbound call to a third party is the project owner's decision, not a
    /// default (ARCHITECTURE.md §19.1 — outbound calls are governed).
    /// </summary>
    public bool UseExternalService { get; set; }

    /// <summary>
    /// The k-anonymity range endpoint. Only the first five characters of the
    /// SHA-1 hash are ever sent, so the service never learns the password or
    /// even its full hash.
    /// </summary>
    public Uri? RangeApiBaseUrl { get; set; } = new("https://api.pwnedpasswords.com/range/");

    /// <summary>
    /// How long to wait. Short by design: screening must never be able to make
    /// signing up or changing a password hang.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);
}

/// <summary>
/// Screens passwords against known-breached lists using k-anonymity.
/// <para>
/// <b>How the k-anonymity range check works.</b> The password is hashed with
/// SHA-1, and only the first <i>five</i> hex characters of that hash are sent.
/// The service returns every known-breached hash suffix sharing that prefix —
/// typically several hundred — and the match is made locally. The service
/// therefore never receives the password, nor its complete hash, nor enough
/// information to identify it. SHA-1 is used here because the published corpus
/// is indexed by SHA-1; it is a lookup key, not a security measure, and has
/// nothing to do with how passwords are stored (that is Argon2id).
/// </para>
/// <para>
/// <b>It fails open, deliberately.</b> If the service is slow, unreachable or
/// returns nonsense, the password is allowed. Screening is a valuable control
/// but it is not worth being unable to create an account or recover one because
/// a third party is having an outage. The failure is logged so it is visible
/// rather than silent.
/// </para>
/// <para>
/// With <see cref="BreachedPasswordOptions.UseExternalService"/> off — the
/// default, and the setting in Development — no outbound call is made and every
/// password passes. That is stated plainly here and in the module documentation,
/// so nobody believes screening is happening when it is not.
/// </para>
/// </summary>
public sealed class BreachedPasswordChecker(
    HttpClient httpClient,
    IOptions<BreachedPasswordOptions> options,
    ILogger<BreachedPasswordChecker> logger) : IBreachedPasswordChecker
{
    private const int PrefixLength = 5;

    private readonly BreachedPasswordOptions _options = options.Value;

    public async Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(password) || !_options.UseExternalService || _options.RangeApiBaseUrl is null)
        {
            return false;
        }

        string hash = Sha1Hex(password);
        string prefix = hash[..PrefixLength];
        string suffix = hash[PrefixLength..];

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.Timeout);

            using HttpResponseMessage response = await httpClient.GetAsync(
                new Uri(_options.RangeApiBaseUrl, prefix), timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Breached-password screening returned {StatusCode}. Allowing the password.",
                    (int)response.StatusCode);

                return false;
            }

            string body = await response.Content.ReadAsStringAsync(timeout.Token);

            return ContainsSuffix(body, suffix);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            // Fails open. A screening outage must not stop people signing up or
            // recovering an account, but it must not be silent either.
            logger.LogWarning(
                exception,
                "Breached-password screening was unavailable. Allowing the password.");

            return false;
        }
    }

    /// <summary>
    /// Scans the response for the hash suffix. Each line is
    /// <c>SUFFIX:COUNT</c>; only the suffix is compared.
    /// </summary>
    private static bool ContainsSuffix(string body, string suffix)
    {
        foreach (ReadOnlySpan<char> line in body.AsSpan().EnumerateLines())
        {
            int separator = line.IndexOf(':');
            ReadOnlySpan<char> candidate = separator < 0 ? line : line[..separator];

            if (candidate.Trim().Equals(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string Sha1Hex(string password)
    {
#pragma warning disable CA5350 // SHA-1 is the corpus index here, not a security measure.
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(password));
#pragma warning restore CA5350

        return Convert.ToHexString(hash).ToUpperInvariant();
    }

    /// <summary>Formats a count for logging without pulling in current culture.</summary>
    internal static string FormatCount(int count) => count.ToString(CultureInfo.InvariantCulture);
}
