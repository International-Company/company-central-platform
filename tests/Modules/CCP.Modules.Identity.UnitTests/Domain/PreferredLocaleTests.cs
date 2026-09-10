using CCP.Modules.Identity.Domain.Users;

namespace CCP.Modules.Identity.UnitTests.Domain;

/// <summary>
/// The language a person is written to in.
/// <para>
/// <b>Small, and it closes a gap that made half the Platform's bilingualism
/// decorative.</b> The portal knows which language it is showing from the URL it
/// was opened at; an email arrives with nobody present to have opened anything.
/// So until this was stored, every notification went out in the company default
/// and an English speaker in an Arabic company received Arabic — in a Platform
/// whose two languages are meant to be equal.
/// </para>
/// </summary>
public sealed class PreferredLocaleTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);

    private static User AUser() =>
        User.Create("someone", "someone@example.invalid", "Someone", Now).Value;

    /// <summary>
    /// A new account has chosen nothing, and that is different from having
    /// chosen the company default.
    /// </summary>
    [Fact]
    public void ANewAccountHasNoPreference()
        => Assert.Null(AUser().PreferredLocale);

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void EitherOfThePlatformsLanguagesIsAccepted(string locale)
    {
        User user = AUser();

        Assert.True(user.ChooseLocale(locale, Now).IsSuccess);
        Assert.Equal(locale, user.PreferredLocale);
    }

    [Theory]
    [InlineData("AR")]
    [InlineData("  en  ")]
    [InlineData("En")]
    public void TheChoiceIsNormalised(string locale)
    {
        User user = AUser();

        Assert.True(user.ChooseLocale(locale, Now).IsSuccess);
        Assert.Equal(locale.Trim().ToLowerInvariant(), user.PreferredLocale);
    }

    /// <summary>
    /// A closed list rather than a pattern.
    /// <para>
    /// <c>fr</c> is a perfectly well-formed language tag and the Platform has no
    /// messages in it. Accepting it would store a preference that silently falls
    /// back for ever, which reads to the person as their choice being ignored —
    /// worse than being told plainly that the Platform speaks two languages.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("fr")]
    [InlineData("ar-SA")]
    [InlineData("en-GB")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("english")]
    public void AnythingElseIsRefused(string locale)
    {
        User user = AUser();

        Assert.True(user.ChooseLocale(locale, Now).IsFailure);
        Assert.Null(user.PreferredLocale);
    }

    /// <summary>
    /// Clearing has to be expressible. Somebody who set English by accident
    /// needs a way back to "whatever everyone else gets", and that is not the
    /// same as setting Arabic — it follows the company if the company changes.
    /// </summary>
    [Fact]
    public void ChoosingNothingClearsThePreference()
    {
        User user = AUser();

        Assert.True(user.ChooseLocale("en", Now).IsSuccess);
        Assert.True(user.ChooseLocale(null, Now).IsSuccess);

        Assert.Null(user.PreferredLocale);
    }

    /// <summary>
    /// A refused choice leaves the previous one alone. Being told "the Platform
    /// speaks two languages" must not also cost somebody the language they had.
    /// </summary>
    [Fact]
    public void ARefusedChoiceDoesNotDisturbTheOneAlreadyMade()
    {
        User user = AUser();

        user.ChooseLocale("ar", Now);
        Assert.True(user.ChooseLocale("de", Now).IsFailure);

        Assert.Equal("ar", user.PreferredLocale);
    }
}
