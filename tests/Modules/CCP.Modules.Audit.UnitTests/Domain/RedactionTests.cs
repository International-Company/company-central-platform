using System.Text.Json;
using CCP.Modules.Audit.Domain;

namespace CCP.Modules.Audit.UnitTests.Domain;

/// <summary>
/// Keeping credentials out of a table nobody can go back and clean.
/// <para>
/// This is the most consequential thing the Audit module does. An audit trail is
/// append-only by design, so a secret written into it is a secret that stays
/// there — there is no update path to remove it and, by intent, no privilege to
/// try.
/// </para>
/// </summary>
public sealed class RedactionTests
{
    [Theory]
    [InlineData("password")]
    [InlineData("Password")]
    [InlineData("newPassword")]
    [InlineData("password_hash")]
    [InlineData("PasswordConfirmation")]
    [InlineData("passphrase")]
    [InlineData("apiKey")]
    [InlineData("api_key")]
    [InlineData("refreshToken")]
    [InlineData("access_token")]
    [InlineData("clientSecret")]
    [InlineData("privateKey")]
    [InlineData("recoveryCode")]
    [InlineData("totpSecret")]
    [InlineData("mfaSecret")]
    [InlineData("Authorization")]
    [InlineData("cookie")]
    public void IsSensitive_CatchesEveryNameACredentialGoesBy(string name)
        => Assert.True(Redaction.IsSensitive(name), $"'{name}' must be treated as sensitive.");

    [Theory]
    [InlineData("username")]
    [InlineData("email")]
    [InlineData("displayName")]
    [InlineData("unitId")]
    [InlineData("status")]
    public void IsSensitive_LeavesOrdinaryFieldsAlone(string name)
        => Assert.False(Redaction.IsSensitive(name), $"'{name}' must not be redacted.");

    [Fact]
    public void Apply_ReplacesASensitiveValue()
    {
        string? result = Redaction.Apply("""{"username":"amira","password":"correct horse battery"}""");

        Assert.NotNull(result);
        Assert.DoesNotContain("correct horse battery", result, StringComparison.Ordinal);
        Assert.Contains(Redaction.Placeholder, result, StringComparison.Ordinal);

        // The surrounding record must survive: redaction that destroyed the
        // event would trade one problem for another.
        Assert.Contains("amira", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_ReachesNestedObjects()
    {
        string? result = Redaction.Apply(
            """{"user":{"name":"amira","credentials":{"password":"hunter2"}}}""");

        Assert.NotNull(result);
        Assert.DoesNotContain("hunter2", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_RedactsAWholeSensitiveSubtree()
    {
        string? result = Redaction.Apply(
            """{"credentials":{"password":"hunter2","salt":"abc","iterations":3}}""");

        Assert.NotNull(result);

        // An object called "credentials" holds nothing worth keeping. Descending
        // into it to redact field by field would preserve exactly the structure
        // an attacker wants.
        Assert.DoesNotContain("hunter2", result, StringComparison.Ordinal);
        Assert.DoesNotContain("iterations", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_ReachesInsideArrays()
    {
        string? result = Redaction.Apply(
            """{"attempts":[{"token":"aaa"},{"token":"bbb"}]}""");

        Assert.NotNull(result);
        Assert.DoesNotContain("aaa", result, StringComparison.Ordinal);
        Assert.DoesNotContain("bbb", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_LeavesTheDocumentValidJson()
    {
        string? result = Redaction.Apply("""{"a":1,"password":"x","b":[1,2,{"secret":"y"}]}""");

        Assert.NotNull(result);

        // The column is jsonb. Producing something that no longer parses would
        // fail the insert and lose the event entirely.
        JsonDocument parsed = JsonDocument.Parse(result);

        Assert.Equal(1, parsed.RootElement.GetProperty("a").GetInt32());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Apply_PassesEmptyInputThrough(string? input)
        => Assert.Equal(input, Redaction.Apply(input));

    [Fact]
    public void Apply_LeavesNonJsonUnchanged()
    {
        const string PlainText = "not json at all";

        // A filter for structured payloads. Mangling free text would be worse
        // than leaving it, and the columns this guards are jsonb.
        Assert.Equal(PlainText, Redaction.Apply(PlainText));
    }
}
