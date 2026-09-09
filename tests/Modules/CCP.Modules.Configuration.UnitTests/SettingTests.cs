using CCP.Kernel.Results;
using CCP.Modules.Configuration.Domain.Settings;

namespace CCP.Modules.Configuration.UnitTests;

/// <summary>
/// Declaring a setting, and what a value has to satisfy.
/// </summary>
public sealed class SettingDefinitionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private static SettingDefinition ADefinition(
        SettingValueType type = SettingValueType.Text, string? defaultValue = null)
        => SettingDefinition.Declare(
            "platform.sessions.timeout", "platform", type, null, defaultValue, Now).Value;

    [Fact]
    public void AKeyMustSitInItsOwnersNamespace()
    {
        // The same rule as permissions, for the same reason: one system must not
        // be able to redefine another's behaviour by declaring a setting in its
        // name.
        Assert.True(SettingDefinition.Declare(
            "finance.invoices.limit", "platform", SettingValueType.Number, null, null, Now)
            .IsFailure);

        Assert.True(SettingDefinition.Declare(
            "finance.invoices.limit", "finance", SettingValueType.Number, null, null, Now)
            .IsSuccess);
    }

    [Fact]
    public void AKeyNeedsThreeParts()
    {
        Assert.True(SettingDefinition.Declare(
            "platform.timeout", "platform", SettingValueType.Text, null, null, Now).IsFailure);
    }

    [Theory]
    [InlineData(SettingValueType.Boolean, "true", true)]
    [InlineData(SettingValueType.Boolean, "yes", false)]
    [InlineData(SettingValueType.Number, "42", true)]
    [InlineData(SettingValueType.Number, "4.2", false)]
    [InlineData(SettingValueType.Duration, "00:15:00", true)]
    [InlineData(SettingValueType.Duration, "fifteen minutes", false)]
    [InlineData(SettingValueType.Url, "https://example.test/hook", true)]
    [InlineData(SettingValueType.Url, "not-a-url", false)]
    [InlineData(SettingValueType.Url, "file:///etc/passwd", false)]
    public void ValuesAreCheckedAgainstTheDeclaredType(
        SettingValueType type, string value, bool expected)
    {
        Assert.Equal(expected, ADefinition(type).Validate(value).IsSuccess);
    }

    [Fact]
    public void ANumberIsBoundedByItsValueAndTextByItsLength()
    {
        SettingDefinition number = ADefinition(SettingValueType.Number);
        number.SetConstraints(1, 100, null, Now);

        Assert.True(number.Validate("50").IsSuccess);
        Assert.True(number.Validate("0").IsFailure);
        Assert.True(number.Validate("101").IsFailure);

        SettingDefinition text = ADefinition();
        text.SetConstraints(2, 5, null, Now);

        Assert.True(text.Validate("abc").IsSuccess);
        Assert.True(text.Validate("a").IsFailure);
        Assert.True(text.Validate("abcdef").IsFailure);
    }

    [Fact]
    public void AClosedListRefusesAnythingElse()
    {
        SettingDefinition definition = ADefinition();
        definition.SetConstraints(null, null, ["debug", "information", "warning"], Now);

        Assert.True(definition.Validate("warning").IsSuccess);
        Assert.True(definition.Validate("Warning").IsFailure);
        Assert.True(definition.Validate("verbose").IsFailure);
    }

    [Fact]
    public void ADefaultIsValidatedLikeAnyOtherValue()
    {
        // A default that could not be set by hand is a setting that is invalid
        // until somebody changes it, which nobody would notice.
        Assert.True(SettingDefinition.Declare(
            "platform.sessions.timeout", "platform", SettingValueType.Duration,
            null, "not-a-duration", Now).IsFailure);
    }

    [Fact]
    public void ConstraintsThatWouldInvalidateTheDefaultAreRefused()
    {
        SettingDefinition definition = ADefinition(SettingValueType.Number, "500");

        Result narrowed = definition.SetConstraints(1, 100, null, Now);

        // A definition whose own default it rejects is a trap for whoever reads
        // it next.
        Assert.True(narrowed.IsFailure);
        Assert.Equal("CONFIG.DEFAULT_VIOLATES_CONSTRAINTS", narrowed.Error.Code);
    }

    [Fact]
    public void AnInvertedRangeIsRefused()
    {
        Assert.True(ADefinition().SetConstraints(100, 1, null, Now).IsFailure);
    }
}

/// <summary>
/// Keeping secrets out of a table that is stored in plaintext, exported, backed
/// up and put on a screen.
/// <para>
/// <b>The most important tests in this module.</b> A secret put here is a secret
/// in all of those places, and the person who put it there did so because it was
/// convenient — which is exactly when it happens.
/// </para>
/// </summary>
public sealed class SecretShapedValueTests
{
    [Theory]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----")]
    [InlineData("sk-proj-abcdefghijklmnop")]
    [InlineData("ccps_9jK1mQ7vT2xR4wZ8")]
    [InlineData("AKIAIOSFODNN7EXAMPLE")]
    [InlineData("ghp_16C7e42F292c6912E7710c838347Ae178B4a")]
    [InlineData("xoxb-123456789012-abcdefghijklmnop")]
    [InlineData("Bearer eyJhbGciOiJIUzI1NiJ9.payload.signature")]
    public void ARecognisableCredentialIsRefused(string value)
    {
        Assert.True(SecretShapedValue.Looks(value));
    }

    [Fact]
    public void AConnectionStringCarryingAPasswordIsRefused()
    {
        // Pasted into settings more often than anything else on the list,
        // because it looks like configuration — and it is, apart from the part
        // that is not.
        Assert.True(SecretShapedValue.Looks(
            "Host=db.example.test;Database=platform;Username=app;Password=hunter2"));

        Assert.True(SecretShapedValue.Looks("api_key=abc123def456"));
    }

    [Fact]
    public void AJsonWebTokenIsRefused()
    {
        Assert.True(SecretShapedValue.Looks(
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NSJ9.dBjftJeZ4CVP"));
    }

    [Fact]
    public void ALongRandomLookingStringIsRefused()
    {
        Assert.True(SecretShapedValue.Looks("aB3xK9mQ7vT2wR4zY8nP5jL1hG6dF0sC"));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("00:15:00")]
    [InlineData("information")]
    [InlineData("26214400")]
    [InlineData("Company Central Platform")]
    [InlineData("ar")]
    [InlineData("")]
    public void OrdinaryConfigurationIsNotRefused(string value)
    {
        Assert.False(SecretShapedValue.Looks(value));
    }

    [Fact]
    public void AUrlIsNotMistakenForASecret()
    {
        // Long, unbroken and legitimate — and there is a setting type for it, so
        // refusing one here would refuse the thing that type exists for.
        Assert.False(SecretShapedValue.Looks(
            "https://company-central-platform-production.up.railway.app/api/v1/health"));
    }

    [Fact]
    public void ASentenceIsNotMistakenForASecret()
    {
        Assert.False(SecretShapedValue.Looks(
            "The Platform sends this message when an approval has been waiting too long"));
    }

    [Fact]
    public void ASettingRefusesASecretWhateverItsSensitivityFlagSays()
    {
        SettingDefinition definition = SettingDefinition.Declare(
            "platform.integrations.token", "platform", SettingValueType.Text,
            null, null, new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero)).Value;

        definition.MarkSensitive(true, new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero));

        Result validated = definition.Validate("sk-proj-abcdefghijklmnopqrstuv");

        // Sensitivity stops a value being read back. It does not stop it being
        // in the database, in the backup, or in the hands of whoever gets a copy.
        Assert.True(validated.IsFailure);
        Assert.Equal("CONFIG.SECRET_SHAPED_VALUE", validated.Error.Code);
    }
}

/// <summary>
/// Setting a value, and what the change history keeps.
/// </summary>
public sealed class SettingValueTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Company = Guid.CreateVersion7();
    private static readonly Guid Actor = Guid.CreateVersion7();

    private static SettingDefinition ADefinition(bool sensitive = false)
    {
        SettingDefinition definition = SettingDefinition.Declare(
            "platform.sessions.timeout", "platform", SettingValueType.Duration,
            null, "00:30:00", Now).Value;

        definition.MarkSensitive(sensitive, Now);

        return definition;
    }

    [Fact]
    public void APlatformValueIsNotQualifiedByAnything()
    {
        Assert.True(SettingValue.Set(
            ADefinition(), SettingScope.Platform, Company, "00:15:00", Now).IsFailure);

        Assert.True(SettingValue.Set(
            ADefinition(), SettingScope.Platform, null, "00:15:00", Now).IsSuccess);
    }

    [Fact]
    public void ACompanyValueHasToSayWhichCompany()
    {
        Assert.True(SettingValue.Set(
            ADefinition(), SettingScope.Company, null, "00:15:00", Now).IsFailure);

        Assert.True(SettingValue.Set(
            ADefinition(), SettingScope.Company, Company, "00:15:00", Now).IsSuccess);
    }

    [Fact]
    public void AnInvalidValueIsRefusedAtEveryScope()
    {
        Assert.True(SettingValue.Set(
            ADefinition(), SettingScope.Platform, null, "half an hour", Now).IsFailure);
    }

    [Fact]
    public void ChangingHandsBackWhatItWas()
    {
        SettingValue value = SettingValue.Set(
            ADefinition(), SettingScope.Platform, null, "00:15:00", Now).Value;

        Result<string> changed = value.Change(ADefinition(), "00:45:00", Now.AddHours(1));

        // The old value is needed by the change history, and reading it from the
        // database afterwards would be reading it after it was overwritten.
        Assert.Equal("00:15:00", changed.Value);
        Assert.Equal("00:45:00", value.Value);
    }

    [Fact]
    public void TheHistoryOfASensitiveSettingSaysThatItChangedAndNotWhatTo()
    {
        SettingChange change = SettingChange.Record(
            ADefinition(sensitive: true), SettingScope.Platform, null,
            "old-value", "new-value", Actor, Now, "rotating");

        // A value that cannot be read back through the API but sits in plain
        // sight in its own change log has not been protected — it has been
        // moved.
        Assert.DoesNotContain("old-value", change.OldValue!, StringComparison.Ordinal);
        Assert.DoesNotContain("new-value", change.NewValue!, StringComparison.Ordinal);

        // What, when and who survive. Those are the questions the history exists
        // for, and none of them needs the value.
        Assert.Equal(Actor, change.ChangedBy);
        Assert.Equal(Now, change.ChangedAt);
        Assert.Equal("rotating", change.Reason);
    }

    [Fact]
    public void AnOrdinarySettingKeepsBothValuesInItsHistory()
    {
        SettingChange change = SettingChange.Record(
            ADefinition(), SettingScope.Platform, null,
            "00:15:00", "00:45:00", Actor, Now, null);

        Assert.Equal("00:15:00", change.OldValue);
        Assert.Equal("00:45:00", change.NewValue);
    }

    [Fact]
    public void AFirstSettingRecordsThatThereWasNoOldValue()
    {
        SettingChange change = SettingChange.Record(
            ADefinition(), SettingScope.Platform, null, null, "00:45:00", Actor, Now, null);

        // Null is a different fact from empty, and the two are worth telling
        // apart six months later.
        Assert.Null(change.OldValue);
    }
}
