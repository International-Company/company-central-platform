using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace CCP.Kernel.Infrastructure.Persistence;

/// <summary>
/// Works out the PostgreSQL connection string, whatever shape the host supplies
/// it in.
/// <para>
/// The Platform's own setting is <c>ConnectionStrings:Platform</c> in Npgsql's
/// key-value form. Most managed platforms instead publish a single
/// <c>DATABASE_URL</c> as a URI — <c>postgresql://user:pass@host:port/db</c> —
/// which <b>Npgsql does not parse</b>. Without a translation the application
/// starts, finds no connection string it recognises, and fails on a value that
/// is sitting right there in the environment.
/// </para>
/// <para>
/// The explicit setting always wins. The URI is a fallback, so a deployment that
/// wants to say exactly what it means still can.
/// </para>
/// </summary>
public static class ConnectionStringResolver
{
    /// <summary>The Platform's own connection string key.</summary>
    public const string PlatformConnectionName = "Platform";

    /// <summary>
    /// The variable managed platforms conventionally publish. Read without the
    /// <c>CCP_</c> prefix because the platform sets it, not us.
    /// </summary>
    public const string DatabaseUrlVariable = "DATABASE_URL";

    /// <summary>
    /// Returns a connection string Npgsql understands, or null if the
    /// configuration carries neither form.
    /// </summary>
    public static string? Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string? explicitValue = configuration.GetConnectionString(PlatformConnectionName);

        if (!string.IsNullOrWhiteSpace(explicitValue))
        {
            return explicitValue;
        }

        string? databaseUrl = configuration[DatabaseUrlVariable]
                           ?? Environment.GetEnvironmentVariable(DatabaseUrlVariable);

        return string.IsNullOrWhiteSpace(databaseUrl) ? null : FromUri(databaseUrl);
    }

    /// <summary>
    /// Translates <c>postgresql://user:password@host:port/database</c> into
    /// Npgsql's key-value form.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The value is present but not a URI we can read. Thrown rather than
    /// returning null, because a malformed <c>DATABASE_URL</c> is a
    /// misconfiguration to be reported, not an absence to be shrugged off.
    /// </exception>
    public static string FromUri(string databaseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseUrl);

        if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out Uri? uri)
            || !uri.Scheme.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{DatabaseUrlVariable} is not a PostgreSQL URI. Expected "
                + "postgresql://user:password@host:port/database.");
        }

        string[] credentials = uri.UserInfo.Split(':', 2);

        // Percent-decoded: a generated password routinely contains characters
        // that must be escaped in a URI, and passing the escaped form through
        // would produce an authentication failure that looks like a wrong
        // password rather than a parsing bug.
        string username = Uri.UnescapeDataString(credentials[0]);
        string password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : string.Empty;

        string database = uri.AbsolutePath.Trim('/');
        int port = uri.Port > 0 ? uri.Port : 5432;

        var builder = new System.Text.StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"Host={uri.Host};")
            .Append(CultureInfo.InvariantCulture, $"Port={port.ToString(CultureInfo.InvariantCulture)};")
            .Append(CultureInfo.InvariantCulture, $"Database={database};")
            .Append(CultureInfo.InvariantCulture, $"Username={username};")
            .Append(CultureInfo.InvariantCulture, $"Password={password};");

        // Managed PostgreSQL terminates plaintext connections, and the
        // certificate is usually issued to an internal hostname that will not
        // validate. Require the encryption; do not demand a name match we
        // cannot satisfy.
        builder.Append("SSL Mode=Require;Trust Server Certificate=true");

        return builder.ToString();
    }
}
