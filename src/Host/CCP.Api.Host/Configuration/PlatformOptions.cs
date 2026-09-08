using System.ComponentModel.DataAnnotations;

namespace CCP.Api.Host.Configuration;

/// <summary>
/// Platform-wide host settings, validated at startup.
/// <para>
/// Bound with <c>ValidateOnStart</c> so a misconfigured deployment fails
/// immediately with a clear message, rather than at an arbitrary later request
/// when someone happens to hit the affected path.
/// </para>
/// <para>
/// This holds configuration, never secrets. Secrets live in the secret manager
/// (ARCHITECTURE.md §12.7).
/// </para>
/// </summary>
public sealed class PlatformOptions
{
    public const string SectionName = "Platform";

    /// <summary>Public base URL of the API, used to build absolute links.</summary>
    [Required]
    [Url]
    public string BaseUrl { get; set; } = "http://localhost:5080";

    /// <summary>
    /// Origins permitted to call the API from a browser. A strict allow-list;
    /// wildcards are prohibited, and an empty list denies all cross-origin
    /// access rather than allowing it.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>Default locale when a request expresses no preference.</summary>
    [Required]
    public string DefaultLocale { get; set; } = "en";

    /// <summary>Locales the Platform serves (ADR-011).</summary>
    public string[] SupportedLocales { get; set; } = ["en", "ar"];
}
