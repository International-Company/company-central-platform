using System.Text;
using System.Text.Json;
using CCP.Kernel.Results;
using CCP.Modules.Integrations.Domain.Logging;
using CCP.Modules.Integrations.Domain.Providers;
using CCP.Modules.Integrations.Domain.Webhooks;

namespace CCP.Modules.Integrations.UnitTests;

/// <summary>
/// What reaches the call log, and what does not.
/// </summary>
public sealed class PayloadRedactorTests
{
    private static readonly string[] Sensitive = ["cardNumber", "password", "credentials"];

    [Fact]
    public void ADeclaredFieldIsBlanked()
    {
        string redacted = PayloadRedactor.Redact(
            """{"amount":100,"cardNumber":"4111111111111111"}""", Sensitive);

        Assert.Contains(PayloadRedactor.Mask, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("4111111111111111", redacted, StringComparison.Ordinal);

        // Everything else survives. A log that redacted the whole payload would
        // be a log nobody can settle a dispute with.
        Assert.Contains("100", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchingIsCaseInsensitive()
    {
        string redacted = PayloadRedactor.Redact("""{"CARDNUMBER":"4111"}""", Sensitive);

        Assert.DoesNotContain("4111", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void ANestedFieldIsBlankedWhereverItAppears()
    {
        // Path-based matching would be more precise and would miss this one
        // level deeper than whoever wrote the policy expected — and the failure
        // mode of being too precise here is a leak.
        string redacted = PayloadRedactor.Redact(
            """{"order":{"payment":{"cardNumber":"4111"}}}""", Sensitive);

        Assert.DoesNotContain("4111", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void AnObjectNamedAsSensitiveIsMaskedWholeRatherThanWalkedInto()
    {
        string redacted = PayloadRedactor.Redact(
            """{"credentials":{"user":"admin","secret":"hunter2"}}""", Sensitive);

        Assert.DoesNotContain("hunter2", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("admin", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void SensitiveFieldsInsideAnArrayAreBlanked()
    {
        string redacted = PayloadRedactor.Redact(
            """{"cards":[{"cardNumber":"4111"},{"cardNumber":"5222"}]}""", Sensitive);

        Assert.DoesNotContain("4111", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("5222", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void TheResultIsStillValidJson()
    {
        string redacted = PayloadRedactor.Redact(
            """{"amount":100,"cardNumber":"4111","items":[1,2,3]}""", Sensitive);

        using JsonDocument document = JsonDocument.Parse(redacted);

        Assert.Equal(100, document.RootElement.GetProperty("amount").GetInt32());
    }

    [Fact]
    public void SomethingThatIsNotJsonIsNotPassedThrough()
    {
        // It cannot be inspected, so it cannot be shown to be safe. Recording
        // its shape is worth less to an investigation and cannot leak anything.
        string redacted = PayloadRedactor.Redact("card=4111111111111111&cvv=123", Sensitive);

        Assert.DoesNotContain("4111111111111111", redacted, StringComparison.Ordinal);
        Assert.Contains("unparsed", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void AVeryLargePayloadIsTruncated()
    {
        string large = $$"""{"note":"{{new string('x', PayloadRedactor.MaxLength * 2)}}"}""";

        string redacted = PayloadRedactor.Redact(large, []);

        Assert.True(redacted.Length < large.Length);
        Assert.Contains("truncated", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingDeclaredMeansNothingBlanked()
    {
        string redacted = PayloadRedactor.Redact("""{"amount":100}""", []);

        Assert.Contains("100", redacted, StringComparison.Ordinal);
    }
}

/// <summary>
/// Whether an inbound webhook really came from the provider it claims to.
/// </summary>
public sealed class WebhookSignatureTests
{
    private const string Secret = "a-signing-secret-from-the-provider";

    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly byte[] Body =
        Encoding.UTF8.GetBytes("""{"event":"payment.succeeded","id":"pay_123"}""");

    private static long Timestamp => Now.ToUnixTimeSeconds();

    [Fact]
    public void ACorrectlySignedWebhookIsValid()
    {
        string signature = WebhookSignature.Compute(Secret, Timestamp, Body);

        Assert.Equal(
            WebhookSignature.Verdict.Valid,
            WebhookSignature.Verify(Secret, signature, Timestamp, Body, Now));
    }

    [Fact]
    public void AMissingSignatureIsRefused()
    {
        Assert.Equal(
            WebhookSignature.Verdict.Missing,
            WebhookSignature.Verify(Secret, null, Timestamp, Body, Now));
    }

    [Fact]
    public void AChangedBodyBreaksTheSignature()
    {
        string signature = WebhookSignature.Compute(Secret, Timestamp, Body);

        byte[] tampered = Encoding.UTF8.GetBytes(
            """{"event":"payment.succeeded","id":"pay_999"}""");

        Assert.Equal(
            WebhookSignature.Verdict.Mismatch,
            WebhookSignature.Verify(Secret, signature, Timestamp, tampered, Now));
    }

    [Fact]
    public void AnotherSecretDoesNotProduceTheSameSignature()
    {
        string signature = WebhookSignature.Compute("someone-elses-secret", Timestamp, Body);

        Assert.Equal(
            WebhookSignature.Verdict.Mismatch,
            WebhookSignature.Verify(Secret, signature, Timestamp, Body, Now));
    }

    [Fact]
    public void AChangedTimestampBreaksTheSignature()
    {
        // The timestamp is inside the signed material, not beside it. If it were
        // only a header, an attacker replaying a captured request would simply
        // change it — and the tolerance window would then be checked against a
        // value they control.
        string signature = WebhookSignature.Compute(Secret, Timestamp, Body);

        Assert.Equal(
            WebhookSignature.Verdict.Mismatch,
            WebhookSignature.Verify(Secret, signature, Timestamp + 1, Body, Now));
    }

    [Fact]
    public void AnOldRequestIsRefusedHoweverWellSigned()
    {
        long old = Now.AddHours(-1).ToUnixTimeSeconds();
        string signature = WebhookSignature.Compute(Secret, old, Body);

        Assert.Equal(
            WebhookSignature.Verdict.StaleTimestamp,
            WebhookSignature.Verify(Secret, signature, old, Body, Now));
    }

    [Fact]
    public void ATimestampFarInTheFutureIsAlsoRefused()
    {
        // Either a broken clock or somebody buying themselves an arbitrarily
        // long replay window.
        long future = Now.AddHours(1).ToUnixTimeSeconds();
        string signature = WebhookSignature.Compute(Secret, future, Body);

        Assert.Equal(
            WebhookSignature.Verdict.StaleTimestamp,
            WebhookSignature.Verify(Secret, signature, future, Body, Now));
    }

    [Fact]
    public void TheWindowIsGenerousEnoughForClockDrift()
    {
        long slightlyOld = Now.AddMinutes(-2).ToUnixTimeSeconds();
        string signature = WebhookSignature.Compute(Secret, slightlyOld, Body);

        Assert.Equal(
            WebhookSignature.Verdict.Valid,
            WebhookSignature.Verify(Secret, signature, slightlyOld, Body, Now));
    }
}

/// <summary>
/// Registering a provider and its operations.
/// </summary>
public sealed class ProviderRegistrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private static IntegrationProvider AProvider() =>
        IntegrationProvider.Create("acme", "Acme Bank", "https://api.acme.test", Now).Value;

    [Fact]
    public void ABaseAddressMustBeAnAbsoluteHttpUrl()
    {
        Assert.True(IntegrationProvider.Create("a", "A", "not-a-url", Now).IsFailure);
        Assert.True(IntegrationProvider.Create("a", "A", "file:///etc/passwd", Now).IsFailure);
        Assert.True(IntegrationProvider.Create("a", "A", "https://ok.test", Now).IsSuccess);
    }

    [Fact]
    public void PastingASecretWhereANameBelongsIsRefused()
    {
        // Accepting it would put a live credential into the database and into
        // every backup of it, which is precisely what references exist to
        // prevent.
        IntegrationProvider provider = AProvider();

        Assert.True(provider.SetCredentialReference(
            "sk-abcdefghijklmnopqrstuvwxyz012345", Now).IsFailure);

        Assert.True(provider.SetCredentialReference(
            new string('x', 120), Now).IsFailure);

        Assert.True(provider.SetCredentialReference(
            "integrations/acme/api-key", Now).IsSuccess);

        Assert.Equal("integrations/acme/api-key", provider.CredentialReference);
    }

    [Fact]
    public void AnAbsolutePathTemplateIsRefused()
    {
        // An absolute URL here would let an endpoint point at a host the
        // provider was never allow-listed for.
        IntegrationProvider provider = AProvider();

        Assert.True(provider.AddEndpoint("a", "POST", "https://evil.test/steal", Now).IsFailure);
        Assert.True(provider.AddEndpoint("b", "POST", "//evil.test/steal", Now).IsFailure);
        Assert.True(provider.AddEndpoint("c", "POST", "../../admin", Now).IsFailure);
        Assert.True(provider.AddEndpoint("d", "POST", "v1/transfer", Now).IsSuccess);
    }

    [Fact]
    public void PathArgumentsAreEscaped()
    {
        IntegrationProvider provider = AProvider();

        IntegrationEndpoint endpoint =
            provider.AddEndpoint("get", "GET", "v1/accounts/{id}", Now).Value;

        // A caller passing an id containing a slash would otherwise be writing
        // path segments of its own.
        Result<string> path = endpoint.BuildPath(
            new Dictionary<string, string> { ["id"] = "../../admin" });

        Assert.True(path.IsSuccess);
        Assert.DoesNotContain("../", path.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingPathArgumentIsRefused()
    {
        IntegrationProvider provider = AProvider();

        IntegrationEndpoint endpoint =
            provider.AddEndpoint("get", "GET", "v1/accounts/{id}", Now).Value;

        Assert.True(endpoint.BuildPath(arguments: null).IsFailure);
    }

    [Fact]
    public void UnreasonableResilienceSettingsAreRefused()
    {
        IntegrationProvider provider = AProvider();

        // More than a handful of retries is not resilience, it is load a
        // struggling provider did not ask for.
        Assert.True(provider.ConfigureResilience(
            TimeSpan.FromSeconds(5), 20, 5, TimeSpan.FromSeconds(30), 8, Now).IsFailure);

        Assert.True(provider.ConfigureResilience(
            TimeSpan.FromMinutes(10), 2, 5, TimeSpan.FromSeconds(30), 8, Now).IsFailure);

        Assert.True(provider.ConfigureResilience(
            TimeSpan.FromSeconds(5), 2, 5, TimeSpan.FromSeconds(30), 8, Now).IsSuccess);
    }

    [Fact]
    public void TheRedactionPolicyIsNormalised()
    {
        IntegrationProvider provider = AProvider();

        provider.SetRedactionPolicy(["  CardNumber ", "password", "password", ""], Now);

        Assert.Equal(["cardnumber", "password"], provider.RedactionPolicy);
    }
}
