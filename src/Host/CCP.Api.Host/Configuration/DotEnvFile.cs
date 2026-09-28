namespace CCP.Api.Host.Configuration;

/// <summary>
/// Reads a <c>.env</c> file into the process environment, for development.
/// <para>
/// <b>The documented way to start this Platform did not work.</b>
/// <c>getting-started.md</c> says to copy <c>.env.example</c> to <c>.env</c>,
/// the failure message when no database is configured points at the same file,
/// and nothing anywhere read one — so following the instructions exactly
/// produced "No database connection is configured", naming a file that had just
/// been created and was being ignored. Found by somebody setting the Platform
/// up from the instructions for the first time.
/// </para>
/// <para>
/// <b>A real environment variable always wins.</b> The file fills gaps and
/// never overrides, so a deployment that sets its own configuration cannot be
/// altered by a file that found its way into an image — and one that did would
/// be a lower priority than the deployment's own settings rather than a higher
/// one.
/// </para>
/// <para>
/// <b>Never in production.</b> Secrets come from the cloud secret manager
/// (ARCHITECTURE.md §12.7), not from a file next to the binary. The container
/// image excludes <c>.env</c> anyway; this refuses to read one even if that
/// ever stops being true.
/// </para>
/// </summary>
public static class DotEnvFile
{
    /// <summary>
    /// Loads <c>.env</c> from the current directory or the nearest parent that
    /// has one, and reports the path if it read anything.
    /// </summary>
    /// <remarks>
    /// Walks upwards because <c>dotnet run --project src/Host/CCP.Api.Host</c>
    /// runs with the repository root as the working directory while
    /// <c>dotnet watch</c> and an IDE do not, and a loader that works from only
    /// one of those is a loader that looks broken half the time.
    /// </remarks>
    public static string? Load(string? startingDirectory = null)
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                "Production",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string? path = Find(startingDirectory ?? Directory.GetCurrentDirectory());

        if (path is null)
        {
            return null;
        }

        foreach (string line in File.ReadAllLines(path))
        {
            string trimmed = line.Trim();

            // Blank lines and comments. A `#` inside a value is not a comment:
            // it is a perfectly ordinary character in a password, and treating
            // it as one would silently truncate the connection string.
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            int separator = trimmed.IndexOf('=', StringComparison.Ordinal);

            if (separator <= 0)
            {
                continue;
            }

            string key = trimmed[..separator].Trim();
            string value = trimmed[(separator + 1)..].Trim();

            // Quotes are a shell convention for holding a value together, not
            // part of it. Stripped only when they surround the whole thing.
            if (value.Length >= 2
                && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            if (key.Length == 0 || Environment.GetEnvironmentVariable(key) is not null)
            {
                continue;
            }

            Environment.SetEnvironmentVariable(key, value);
        }

        return path;
    }

    private static string? Find(string directory)
    {
        var current = new DirectoryInfo(directory);

        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, ".env");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }
}
