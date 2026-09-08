using CCP.Modules.Identity.Domain.Sessions;

namespace CCP.Modules.Identity.UnitTests.Domain;

/// <summary>
/// The refresh-token state machine underpins reuse detection, which ADR-006
/// calls the highest-value control in the authentication design. Its
/// invariants are pinned here.
/// </summary>
public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    private static RefreshToken Issue() =>
        RefreshToken.Issue(Guid.CreateVersion7(), Guid.CreateVersion7(), "hash-value", Now, Lifetime);

    [Fact]
    public void FreshToken_IsUsable()
    {
        Assert.True(Issue().IsUsable(Now));
    }

    [Fact]
    public void SpentToken_IsNotUsable()
    {
        // The core of rotation: once exchanged, a token is finished. Presenting
        // it again is the signal that it was copied.
        RefreshToken token = Issue();

        token.MarkUsed(Guid.CreateVersion7(), Now.AddMinutes(5));

        Assert.True(token.IsUsed);
        Assert.False(token.IsUsable(Now.AddMinutes(5)));
    }

    [Fact]
    public void RevokedToken_IsNotUsable()
    {
        RefreshToken token = Issue();

        token.Revoke(SessionRevocationReasons.UserSignedOut, Now);

        Assert.True(token.IsRevoked);
        Assert.False(token.IsUsable(Now));
    }

    [Fact]
    public void ExpiredToken_IsNotUsable()
    {
        RefreshToken token = Issue();

        Assert.False(token.IsUsable(Now.Add(Lifetime)));
        Assert.False(token.IsUsable(Now.Add(Lifetime).AddSeconds(1)));
    }

    [Fact]
    public void Rotation_KeepsTheFamilyAndChainsTheTokens()
    {
        // Family identity is what lets reuse detection revoke every descendant
        // of one sign-in, rather than only the token that was replayed.
        RefreshToken first = Issue();

        RefreshToken second = RefreshToken.IssueInFamily(
            first.SessionId, first.UserId, first.FamilyId, "second-hash", Now.AddMinutes(5), Lifetime);

        first.MarkUsed(second.Id, Now.AddMinutes(5));

        Assert.Equal(first.FamilyId, second.FamilyId);
        Assert.Equal(second.Id, first.ReplacedByTokenId);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void EachSignIn_StartsANewFamily()
    {
        // Two separate sign-ins must be independently revocable: ending one
        // must not end the other.
        RefreshToken first = Issue();
        RefreshToken second = Issue();

        Assert.NotEqual(first.FamilyId, second.FamilyId);
    }

    [Fact]
    public void Revoke_IsIdempotentAndKeepsTheFirstReason()
    {
        // The first revocation is the one that matters to an investigation, so
        // a later one must not overwrite it.
        RefreshToken token = Issue();

        token.Revoke(SessionRevocationReasons.RefreshTokenReuseDetected, Now);
        token.Revoke(SessionRevocationReasons.UserSignedOut, Now.AddHours(1));

        Assert.Equal(SessionRevocationReasons.RefreshTokenReuseDetected, token.RevokedReason);
        Assert.Equal(Now, token.RevokedAt);
    }

    [Fact]
    public void TokenHash_IsStoredRatherThanTheToken()
    {
        // The entity holds only a hash, so a leaked backup yields nothing
        // usable — the same reasoning applied to passwords.
        RefreshToken token = RefreshToken.Issue(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "sha256-of-the-token", Now, Lifetime);

        Assert.Equal("sha256-of-the-token", token.TokenHash);
    }
}

/// <summary>Session lifetime rules: revocation, the idle timeout and the absolute ceiling.</summary>
public sealed class SessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(12);

    private static Session Start() =>
        Session.Start(Guid.CreateVersion7(), "203.0.113.10", "Firefox", "device-1", Now, AbsoluteLifetime);

    [Fact]
    public void NewSession_IsActive()
    {
        Assert.True(Start().IsActive(Now, IdleTimeout));
    }

    [Fact]
    public void RevokedSession_IsNotActive()
    {
        Session session = Start();

        session.Revoke(SessionRevocationReasons.UserSignedOut, Now);

        Assert.False(session.IsActive(Now, IdleTimeout));
    }

    [Fact]
    public void IdleSession_IsNotActive()
    {
        // The control that matters for an unattended browser.
        Session session = Start();

        Assert.True(session.IsActive(Now.AddHours(11), IdleTimeout));
        Assert.False(session.IsActive(Now.AddHours(13), IdleTimeout));
    }

    [Fact]
    public void Activity_ExtendsTheIdleWindow()
    {
        Session session = Start();

        session.Touch(Now.AddHours(11));

        Assert.True(session.IsActive(Now.AddHours(13), IdleTimeout));
    }

    [Fact]
    public void AbsoluteCeiling_EndsTheSessionHoweverActive()
    {
        // Continuous use must not extend a session forever.
        Session session = Start();

        session.Touch(Now.Add(AbsoluteLifetime).AddMinutes(-1));

        Assert.False(session.IsActive(Now.Add(AbsoluteLifetime).AddSeconds(1), IdleTimeout));
    }

    [Fact]
    public void Revoke_IsIdempotentAndKeepsTheFirstReason()
    {
        Session session = Start();

        session.Revoke(SessionRevocationReasons.RefreshTokenReuseDetected, Now);
        session.Revoke(SessionRevocationReasons.UserSignedOut, Now.AddHours(1));

        Assert.Equal(SessionRevocationReasons.RefreshTokenReuseDetected, session.RevokedReason);
        Assert.Equal(Now, session.RevokedAt);
    }

    [Fact]
    public void LongUserAgent_IsTruncated()
    {
        // User agent is attacker-controlled input that reaches storage and logs.
        Session session = Session.Start(
            Guid.CreateVersion7(), "203.0.113.10", new string('x', 5000), null, Now, AbsoluteLifetime);

        Assert.NotNull(session.UserAgent);
        Assert.True(session.UserAgent.Length <= 512);
    }
}
