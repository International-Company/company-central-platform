using CCP.Kernel.Api.Security;
using CCP.Modules.Security.Domain;

namespace CCP.Architecture.Tests;

/// <summary>
/// The two spellings of the MFA-enrolment refusal stay the same spelling.
/// <para>
/// The kernel writes the response body and may not reference a module (§6.2);
/// the Security module owns the error. So the code exists twice, once as
/// <see cref="PlatformAuthorizationResultHandler.MfaEnrolmentRequiredCode"/> and
/// once as <c>SecurityErrors.MfaRequiredByPolicy.Code</c>.
/// </para>
/// <para>
/// <b>A value in two places goes stale in one of them</b>, and the copy is never
/// the one somebody thinks to update. That is not a guess about this codebase:
/// the phase count lived in the README and the status document and was wrong in
/// the README; the restore-verification claim lived in the register and the
/// README and was wrong in both. Here it would be quieter still — a client
/// matching on the documented code would stop recognising the refusal, and
/// nothing would fail.
/// </para>
/// </summary>
public sealed class StepUpRefusalCodesTests
{
    [Fact]
    public void TheKernelAndSecurityAgreeOnTheEnrolmentCode()
    {
        Assert.Equal(
            PlatformAuthorizationResultHandler.MfaEnrolmentRequiredCode,
            SecurityErrors.MfaRequiredByPolicy.Code);
    }

    /// <summary>
    /// The two refusals are different codes.
    /// <para>
    /// The whole point is that a caller can tell "enrol first" from "confirm
    /// again". Made equal by a careless edit, both branches would still work and
    /// the distinction would silently stop existing — which is the state this
    /// change was written to end.
    /// </para>
    /// </summary>
    [Fact]
    public void TheTwoStepUpRefusalsAreDistinct()
    {
        Assert.NotEqual(
            PlatformAuthorizationResultHandler.StepUpRequiredCode,
            PlatformAuthorizationResultHandler.MfaEnrolmentRequiredCode);

        Assert.NotEqual(
            StepUpRequirement.FailureReason,
            StepUpRequirement.EnrolmentRequiredReason);
    }
}
