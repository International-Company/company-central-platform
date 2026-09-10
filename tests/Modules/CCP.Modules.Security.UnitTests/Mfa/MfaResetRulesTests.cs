using CCP.Kernel.Results;
using CCP.Modules.Security.Domain;
using CCP.Modules.Security.Domain.Events;

namespace CCP.Modules.Security.UnitTests.Mfa;

/// <summary>
/// The rules around clearing somebody else's second factor.
/// <para>
/// <b>Until this existed there was no way back for somebody who had lost both
/// their phone and their recovery codes.</b> Disabling a factor requires proving
/// it, and the recovery codes exist precisely for the case where the phone is
/// gone — so losing both left an account permanently unusable, with the person
/// unable to sign in and nobody able to help. That is not a security property;
/// it is a locked door with the key inside.
/// </para>
/// <para>
/// It is also the most dangerous action the Platform offers, so what is asserted
/// here is mostly what it refuses.
/// </para>
/// </summary>
public sealed class MfaResetRulesTests
{
    /// <summary>
    /// A reset is recorded under its own event type, not as an ordinary
    /// disable.
    /// <para>
    /// The distinction is the whole point. One is somebody managing their own
    /// account; the other is somebody else's protection being taken away, which
    /// is both a legitimate recovery and the exact shape of an insider taking
    /// over an account. A shared type would leave an investigation unable to
    /// tell which had happened — which is the only question worth asking.
    /// </para>
    /// </summary>
    [Fact]
    public void AResetIsADifferentEventFromADisable()
        => Assert.NotEqual(SecurityEventTypes.MfaDisabled, SecurityEventTypes.MfaReset);

    /// <summary>
    /// Both are named rather than merely different, so a rename of either that
    /// collapsed them together would fail here rather than quietly in an
    /// investigation months later.
    /// </summary>
    [Fact]
    public void BothEventTypesKeepTheirNames()
    {
        Assert.Equal("mfa.disabled", SecurityEventTypes.MfaDisabled);
        Assert.Equal("mfa.reset_by_administrator", SecurityEventTypes.MfaReset);
    }

    /// <summary>
    /// Refusing a self-reset is a <b>forbidden</b>, not a validation error.
    /// <para>
    /// It is not a badly-shaped request; it is an act the caller may not
    /// perform. Somebody who still holds their factor uses the ordinary path,
    /// which proves it. Somebody who does not needs a second person — and that
    /// requirement is the control, not an inconvenience around it.
    /// </para>
    /// </summary>
    [Fact]
    public void ResettingOnesOwnFactorIsForbiddenRatherThanInvalid()
        => Assert.Equal(ErrorType.Forbidden, SecurityErrors.CannotResetOwnMfa.Type);

    /// <summary>
    /// The missing reason is a validation error, because it is a badly-shaped
    /// request and the caller can fix it by saying why.
    /// </summary>
    [Fact]
    public void AMissingReasonIsAValidationError()
    {
        Assert.Equal(ErrorType.Validation, SecurityErrors.ResetReasonRequired.Type);
        Assert.Equal("reason", SecurityErrors.ResetReasonRequired.Field);
    }

    /// <summary>
    /// Every error the Platform returns carries a code a client acts on, and
    /// these two mean different things: one says try the other endpoint, the
    /// other says fill in the field.
    /// </summary>
    [Fact]
    public void TheTwoRefusalsAreDistinguishableByCode()
    {
        Assert.Equal("SECURITY.CANNOT_RESET_OWN_MFA", SecurityErrors.CannotResetOwnMfa.Code);
        Assert.Equal("SECURITY.RESET_REASON_REQUIRED", SecurityErrors.ResetReasonRequired.Code);
    }
}
