using CCP.Kernel.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;

namespace CCP.Kernel.UnitTests.Persistence;

/// <summary>
/// Translating a platform-supplied <c>DATABASE_URL</c> into something Npgsql
/// understands.
/// </summary>
public sealed class ConnectionStringResolverTests
{
    [Theory]
    [InlineData("postgresql://user:pass@db.internal:5432/ccp")]
    [InlineData("postgres://user:pass@db.internal:5432/ccp")]
    public void FromUri_AcceptsBothSchemes(string url)
    {
        string result = ConnectionStringResolver.FromUri(url);

        Assert.Contains("Host=db.internal;", result, StringComparison.Ordinal);
        Assert.Contains("Port=5432;", result, StringComparison.Ordinal);
        Assert.Contains("Database=ccp;", result, StringComparison.Ordinal);
        Assert.Contains("Username=user;", result, StringComparison.Ordinal);
        Assert.Contains("Password=pass;", result, StringComparison.Ordinal);
    }

    [Fact]
    public void FromUri_DecodesAnEscapedPassword()
    {
        string result = ConnectionStringResolver.FromUri(
            "postgresql://user:p%40ss%3Aword%2F1@host:5432/db");

        // A generated password routinely contains characters that must be
        // escaped in a URI. Passing the escaped form through would fail
        // authentication in a way that looks like a wrong password rather than
        // a parsing bug — which is a long evening.
        Assert.Contains("Password=p@ss:word/1;", result, StringComparison.Ordinal);
    }

    [Fact]
    public void FromUri_DefaultsThePort()
    {
        string result = ConnectionStringResolver.FromUri("postgresql://user:pass@host/db");

        Assert.Contains("Port=5432;", result, StringComparison.Ordinal);
    }

    [Fact]
    public void FromUri_RequiresEncryptionWithoutDemandingANameMatch()
    {
        string result = ConnectionStringResolver.FromUri("postgresql://user:pass@host:5432/db");

        // Managed PostgreSQL refuses plaintext, and its certificate is usually
        // issued to an internal hostname that will not validate.
        Assert.Contains("SSL Mode=Require", result, StringComparison.Ordinal);
        Assert.Contains("Trust Server Certificate=true", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("mysql://user:pass@host:3306/db")]
    [InlineData("not a uri at all")]
    public void FromUri_RefusesAnythingElse(string url)
    {
        // Thrown rather than returning null: a malformed DATABASE_URL is a
        // misconfiguration to report, not an absence to shrug off.
        Assert.Throws<InvalidOperationException>(() => ConnectionStringResolver.FromUri(url));
    }

    [Fact]
    public void Resolve_PrefersTheExplicitSetting()
    {
        IConfiguration configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Platform"] = "Host=explicit;Database=chosen",
            ["DATABASE_URL"] = "postgresql://user:pass@ignored:5432/ignored"
        });

        // A deployment that says exactly what it means must not be overridden by
        // a convention.
        Assert.Equal("Host=explicit;Database=chosen", ConnectionStringResolver.Resolve(configuration));
    }

    [Fact]
    public void Resolve_FallsBackToTheUri()
    {
        IConfiguration configuration = Build(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://user:pass@host:5432/db"
        });

        string? result = ConnectionStringResolver.Resolve(configuration);

        Assert.NotNull(result);
        Assert.Contains("Host=host;", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_ReturnsNullWhenNeitherIsPresent()
        => Assert.Null(ConnectionStringResolver.Resolve(Build([])));

    private static IConfiguration Build(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
