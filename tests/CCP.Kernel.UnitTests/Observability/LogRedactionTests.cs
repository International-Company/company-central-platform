using CCP.Kernel.Api.Observability;
using Serilog.Events;
using Serilog.Parsing;

namespace CCP.Kernel.UnitTests.Observability;

/// <summary>
/// Credentials removed from log events on the way out.
/// <para>
/// <b>The architecture asks for this at the sink rather than in each log
/// statement</b> (§22.2), and these tests are what makes that claim true rather
/// than intended. Every leak of this kind is written by somebody being careful
/// who did not know the object they logged carried a token three properties
/// down.
/// </para>
/// </summary>
public sealed class LogRedactionTests
{
    private static LogEvent AnEvent(params (string Name, object? Value)[] properties)
    {
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            exception: null,
            new MessageTemplate("test", []),
            []);

        foreach ((string name, object? value) in properties)
        {
            logEvent.AddOrUpdateProperty(
                new LogEventProperty(name, new ScalarValue(value)));
        }

        return logEvent;
    }

    private static string? Read(LogEvent logEvent, string name)
        => logEvent.Properties.TryGetValue(name, out LogEventPropertyValue? value)
           && value is ScalarValue scalar
            ? scalar.Value?.ToString()
            : null;

    [Theory]
    [InlineData("Password")]
    [InlineData("password")]
    [InlineData("RefreshToken")]
    [InlineData("accessToken")]
    [InlineData("ClientSecret")]
    [InlineData("Authorization")]
    [InlineData("ApiKey")]
    [InlineData("RecoveryCode")]
    [InlineData("ConnectionString")]
    public void APropertyWhoseNameMeansACredentialIsBlanked(string name)
    {
        LogEvent logEvent = AnEvent((name, "the-actual-value"));

        new LogRedactionEnricher().Enrich(logEvent, propertyFactory: null!);

        Assert.Equal(LogRedactionEnricher.Mask, Read(logEvent, name));
    }

    [Fact]
    public void AnOrdinaryPropertySurvives()
    {
        // A redactor that blanked everything would be a log nobody can use, and
        // would be switched off within a week.
        LogEvent logEvent = AnEvent(
            ("Username", "ahmad"),
            ("CorrelationId", "01JBQ8Z3"),
            ("DurationMs", 42));

        new LogRedactionEnricher().Enrich(logEvent, propertyFactory: null!);

        Assert.Equal("ahmad", Read(logEvent, "Username"));
        Assert.Equal("01JBQ8Z3", Read(logEvent, "CorrelationId"));
        Assert.Equal("42", Read(logEvent, "DurationMs"));
    }

    [Fact]
    public void AValueThatLooksLikeACredentialIsBlankedWhateverItWasCalled()
    {
        // The case the name check cannot reach: an exception message, a URL with
        // a token in it, a property somebody called something innocuous.
        LogEvent logEvent = AnEvent(
            ("Detail", "Bearer " + "abcdefghijklmnopqrstuvwxyz012345"));

        new LogRedactionEnricher().Enrich(logEvent, propertyFactory: null!);

        Assert.Equal(LogRedactionEnricher.Mask, Read(logEvent, "Detail"));
    }

    [Theory]
    [InlineData("eyJ", "hbGciOiJIUzI1NiJ9.eyJzdWIiOiJub3RyZWFsIn0.notarealsignature")]
    [InlineData("-----BEGIN", " RSA PRIVATE KEY-----")]
    [InlineData("ccps_", "notarealsecretvaluehere")]
    [InlineData("sk-", "notarealkeyvaluehere0000")]
    [InlineData("AKIA", "NOTAREALACCESSKEY123")]
    public void KnownCredentialShapesAreRecognised(string prefix, string rest)
    {
        // Assembled at run time rather than written as literals: the repository
        // is scanned for secrets on every push, and a convincing token in a test
        // file is exactly what that scanner exists to find.
        Assert.True(LogRedactionEnricher.LooksLikeCredential(prefix + rest));
    }

    [Theory]
    [InlineData("a short string")]
    [InlineData("https://company-central-platform.example/api/v1/users")]
    [InlineData("The approval has been waiting for three days")]
    [InlineData("")]
    public void OrdinaryTextIsNotMistakenForACredential(string value)
    {
        Assert.False(LogRedactionEnricher.LooksLikeCredential(value));
    }
}
