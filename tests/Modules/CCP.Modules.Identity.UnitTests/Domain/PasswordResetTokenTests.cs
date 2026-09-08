using CCP.Modules.Identity.Domain.Credentials;

namespace CCP.Modules.Identity.UnitTests.Domain;

/// <summary>
/// A reset token is a full account-takeover credential for as long as it lives,
/// so its lifecycle rules are pinned rather than assumed.
/// </summary>
public sealed class PasswordResetTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private static PasswordResetToken Issue() =>
        PasswordResetToken.Issue(Guid.CreateVersion7(), "hash-value", Now, Lifetime, "203.0.113.10");

    [Fact]
    public void FreshToken_IsUsable()
    {
        Assert.True(Issue().IsUsable(Now));
    }

    [Fact]
    public void SpentToken_IsNotUsable()
    {
        // Single use. A reset link that works twice is a link that works again
        // for whoever else has seen the email.
        PasswordResetToken token = Issue();

        token.MarkUsed(Now.AddMinutes(1));

        Assert.True(token.IsUsed);
        Assert.False(token.IsUsable(Now.AddMinutes(1)));
    }

    [Fact]
    public void InvalidatedToken_IsNotUsable()
    {
        PasswordResetToken token = Issue();

        token.Invalidate(Now);

        Assert.False(token.IsUsable(Now));
    }

    [Fact]
    public void ExpiredToken_IsNotUsable()
    {
        PasswordResetToken token = Issue();

        Assert.True(token.IsUsable(Now.AddMinutes(29)));
        Assert.False(token.IsUsable(Now.Add(Lifetime)));
        Assert.False(token.IsUsable(Now.Add(Lifetime).AddSeconds(1)));
    }

    [Fact]
    public void Invalidating_DoesNotOverwriteRedemption()
    {
        // A token that was actually used must keep recording that it was used.
        // Overwriting with "invalidated" would lose the fact that a reset
        // happened, which matters when reconstructing an incident.
        PasswordResetToken token = Issue();

        token.MarkUsed(Now.AddMinutes(1));
        token.Invalidate(Now.AddMinutes(2));

        Assert.NotNull(token.UsedAt);
        Assert.Null(token.InvalidatedAt);
    }

    [Fact]
    public void Invalidating_IsIdempotent()
    {
        PasswordResetToken token = Issue();

        token.Invalidate(Now);
        token.Invalidate(Now.AddMinutes(5));

        Assert.Equal(Now, token.InvalidatedAt);
    }

    [Fact]
    public void OnlyTheHashIsStored()
    {
        PasswordResetToken token = PasswordResetToken.Issue(
            Guid.CreateVersion7(), "sha256-of-the-token", Now, Lifetime, null);

        Assert.Equal("sha256-of-the-token", token.TokenHash);
    }

    [Fact]
    public void RequestOriginIsRecorded()
    {
        // So a pattern of reset requests against many accounts from one address
        // is visible rather than invisible.
        Assert.Equal("203.0.113.10", Issue().RequestedFromIp);
    }
}
