using CCP.Kernel.Results;
using CCP.Modules.Identity.Domain.Credentials;
using CCP.Modules.Identity.Domain.Users;

namespace CCP.Modules.Identity.UnitTests.Domain;

public sealed class PasswordPolicyTests
{
    private static readonly PasswordPolicy Policy = PasswordPolicy.Default;

    [Fact]
    public void AcceptsALongPassphrase()
    {
        Assert.True(Policy.Validate("correct horse battery staple", "ahmad").IsSuccess);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("elevenchar")]
    public void RejectsAPasswordBelowTheMinimumLength(string password)
    {
        Result result = Policy.Validate(password, "ahmad");

        Assert.True(result.IsFailure);
        Assert.Equal("IDENTITY.PASSWORD_TOO_SHORT", result.Error.Code);
    }

    [Fact]
    public void RejectsAnEmptyPassword()
    {
        Assert.Equal("IDENTITY.PASSWORD_REQUIRED", Policy.Validate("", "ahmad").Error.Code);
    }

    [Fact]
    public void RejectsAnExcessivelyLongPassword()
    {
        // Not a security limit: Argon2id cost scales with input, so an unbounded
        // password is a cheap way to make the server do expensive work.
        Result result = Policy.Validate(new string('x', 500), "ahmad");

        Assert.True(result.IsFailure);
        Assert.Equal("IDENTITY.PASSWORD_TOO_LONG", result.Error.Code);
    }

    [Fact]
    public void RejectsAPasswordContainingTheUsername()
    {
        Result result = Policy.Validate("ahmad-is-my-password", "ahmad");

        Assert.True(result.IsFailure);
        Assert.Equal("IDENTITY.PASSWORD_CONTAINS_USERNAME", result.Error.Code);
    }

    [Fact]
    public void UsernameCheckIsCaseInsensitive()
    {
        Assert.True(Policy.Validate("AHMAD-is-my-password", "ahmad").IsFailure);
    }

    [Fact]
    public void AcceptsAPasswordWithoutCompositionVariety()
    {
        // Deliberate: NIST SP 800-63B advises against composition rules. They
        // push people toward "Password1!" and toward writing passwords down,
        // while adding little real entropy. Length and breach screening do the
        // work instead.
        Assert.True(Policy.Validate("thisisallverylowercase", "ahmad").IsSuccess);
    }

    [Fact]
    public void ReportsEveryProblemAtOnce()
    {
        // A user should not have to fix one field, resubmit, and discover the
        // next. The error contract carries a list for exactly this.
        Result result = Policy.Validate("ahmad", "ahmad");

        Assert.True(result.IsFailure);
        Assert.True(result.Errors.Count >= 2);
        Assert.Contains(result.Errors, e => e.Code == "IDENTITY.PASSWORD_TOO_SHORT");
        Assert.Contains(result.Errors, e => e.Code == "IDENTITY.PASSWORD_CONTAINS_USERNAME");
    }

    [Fact]
    public void ExpiryIsOffByDefault()
    {
        // Forced rotation makes people pick weaker, more predictable passwords
        // and is no longer recommended. Rotate on evidence of compromise.
        var now = new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);

        Assert.False(Policy.IsExpired(now.AddYears(-5), now));
    }

    [Fact]
    public void ExpiryAppliesWhenConfigured()
    {
        var policy = new PasswordPolicy { MaximumAge = TimeSpan.FromDays(90) };
        var now = new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);

        Assert.True(policy.IsExpired(now.AddDays(-91), now));
        Assert.False(policy.IsExpired(now.AddDays(-89), now));
        Assert.False(policy.IsExpired(null, now));
    }
}

public sealed class UserCreationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreatesAValidUser()
    {
        Result<User> result = User.Create("ahmad", "Ahmad@Example.COM", "  Ahmad Mazrou  ", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("ahmad", result.Value.Username);

        // Email normalised to lower case so uniqueness is meaningful.
        Assert.Equal("ahmad@example.com", result.Value.Email);
        Assert.Equal("Ahmad Mazrou", result.Value.DisplayName);
        Assert.Equal(UserStatus.Active, result.Value.Status);
        Assert.False(result.Value.EmailVerified);
    }

    [Fact]
    public void NewUserMustChangePasswordByDefault()
    {
        // An administrator-chosen password must never be a lasting credential.
        Assert.True(User.Create("ahmad", "ahmad@example.com", "Ahmad", Now).Value.MustChangePassword);
    }

    [Fact]
    public void RaisesACreatedEvent()
    {
        User user = User.Create("ahmad", "ahmad@example.com", "Ahmad", Now).Value;

        Assert.Single(user.DomainEvents);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]
    [InlineData("this-username-is-far-too-long-to-be-accepted-by-the-platform-rules-x")]
    [InlineData("ahmad mazrou")]
    [InlineData("ahmad@example")]
    [InlineData("ahmad;DROP TABLE users")]
    [InlineData("ahmad<script>")]
    public void RejectsAnInvalidUsername(string username)
    {
        // An allow-list: usernames reach logs, audit records and URLs, so
        // anything not plainly safe is refused.
        Assert.True(User.Create(username, "ahmad@example.com", "Ahmad", Now).IsFailure);
    }

    [Theory]
    [InlineData("ahmad")]
    [InlineData("ahmad.mazrou")]
    [InlineData("ahmad-mazrou")]
    [InlineData("ahmad_mazrou")]
    [InlineData("ahmad123")]
    public void AcceptsAValidUsername(string username)
    {
        Assert.True(User.Create(username, "ahmad@example.com", "Ahmad", Now).IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("@example.com")]
    [InlineData("ahmad@")]
    [InlineData("ahmad@@example.com")]
    [InlineData("ahmad @example.com")]
    [InlineData("ahmad@example")]
    public void RejectsAnInvalidEmail(string email)
    {
        Assert.True(User.Create("ahmad", email, "Ahmad", Now).IsFailure);
    }

    [Fact]
    public void ReportsEveryFieldProblemAtOnce()
    {
        Result<User> result = User.Create("", "bad-email", "", Now);

        Assert.True(result.IsFailure);
        Assert.True(result.Errors.Count >= 3);
    }

    [Fact]
    public void ChangingEmailResetsVerification()
    {
        // Otherwise changing the address would be a way to bypass verification.
        User user = User.Create("ahmad", "ahmad@example.com", "Ahmad", Now).Value;

        user.MarkEmailVerified(Now);
        Assert.True(user.EmailVerified);

        Assert.True(user.ChangeEmail("new@example.com", Now).IsSuccess);
        Assert.False(user.EmailVerified);
    }

    [Fact]
    public void SettingTheSameEmailKeepsVerification()
    {
        User user = User.Create("ahmad", "ahmad@example.com", "Ahmad", Now).Value;

        user.MarkEmailVerified(Now);

        // Same address in different case is the same address.
        Assert.True(user.ChangeEmail("AHMAD@example.com", Now).IsSuccess);
        Assert.True(user.EmailVerified);
    }
}
