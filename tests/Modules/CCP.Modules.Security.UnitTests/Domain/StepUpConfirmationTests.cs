using CCP.Modules.Security.Domain.Mfa;

namespace CCP.Modules.Security.UnitTests.Domain;

/// <summary>Step-up elevation: bound to a session, absolute, revocable.</summary>
public sealed class StepUpConfirmationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Validity = TimeSpan.FromMinutes(15);

    [Fact]
    public void Issue_IsValidImmediatelyAndUntilExpiry()
    {
        StepUpConfirmation confirmation =
            StepUpConfirmation.Issue(Guid.NewGuid(), Guid.NewGuid(), Now, Validity);

        Assert.True(confirmation.IsValidAt(Now));
        Assert.True(confirmation.IsValidAt(Now.AddMinutes(14).AddSeconds(59)));
    }

    [Fact]
    public void Issue_LapsesOnASchedule_NotOnUse()
    {
        StepUpConfirmation confirmation =
            StepUpConfirmation.Issue(Guid.NewGuid(), Guid.NewGuid(), Now, Validity);

        // Absolute, not sliding. A sliding window would keep a walked-away-from
        // session elevated indefinitely as long as it kept being used, which is
        // the opposite of the intent.
        Assert.Equal(Now.Add(Validity), confirmation.ExpiresAt);
        Assert.False(confirmation.IsValidAt(Now.Add(Validity)));
        Assert.False(confirmation.IsValidAt(Now.AddHours(1)));
    }

    [Fact]
    public void Revoke_EndsElevationBeforeExpiry()
    {
        StepUpConfirmation confirmation =
            StepUpConfirmation.Issue(Guid.NewGuid(), Guid.NewGuid(), Now, Validity);

        confirmation.Revoke(Now.AddMinutes(1));

        // The point of holding this in the database rather than in a token:
        // disabling MFA must be able to end elevation at once.
        Assert.False(confirmation.IsValidAt(Now.AddMinutes(2)));
    }

    [Fact]
    public void Revoke_KeepsTheFirstRevocationTime()
    {
        StepUpConfirmation confirmation =
            StepUpConfirmation.Issue(Guid.NewGuid(), Guid.NewGuid(), Now, Validity);

        confirmation.Revoke(Now.AddMinutes(1));
        confirmation.Revoke(Now.AddMinutes(5));

        Assert.Equal(Now.AddMinutes(1), confirmation.RevokedAt);
    }

    [Fact]
    public void Issue_RecordsTheSessionItWasProvenOn()
    {
        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        StepUpConfirmation confirmation =
            StepUpConfirmation.Issue(userId, sessionId, Now, Validity);

        // Elevation belongs to one session. A confirmation on a laptop must not
        // privilege a stolen token from a different browser.
        Assert.Equal(sessionId, confirmation.SessionId);
        Assert.Equal(userId, confirmation.UserId);
    }
}
