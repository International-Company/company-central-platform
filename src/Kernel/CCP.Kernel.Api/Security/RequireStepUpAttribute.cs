using Microsoft.AspNetCore.Authorization;

namespace CCP.Kernel.Api.Security;

/// <summary>
/// Declares that an endpoint requires a recent second-factor confirmation, not
/// merely a valid session (ARCHITECTURE.md §12.5).
/// <para>
/// <b>This is how MFA is enforced for privileged work.</b> The Platform does not
/// refuse sign-in to an administrator without a second factor — that would lock
/// people out of a system they are already entitled to use, and would make
/// enrolling one impossible. Instead the factor is demanded at the point where
/// it matters: creating users, granting roles, resetting other people's
/// passwords, restructuring the organization. An administrator with no MFA can
/// sign in and read; they cannot act.
/// </para>
/// <para>
/// Enforcing at the action rather than at the door also survives the case the
/// door cannot see: a session stolen after a legitimate sign-in. The token is
/// valid, the session is real, and step-up still stops it.
/// </para>
/// <para>
/// Used <i>alongside</i> <see cref="RequirePermissionAttribute"/>, never instead
/// of it. Two authorize attributes mean two policies, and the framework requires
/// both to pass — permission answers "may this person do this at all", step-up
/// answers "are they still here, right now".
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class RequireStepUpAttribute : Attribute, IAuthorizeData
{
    /// <summary>
    /// The policy name recognised by the policy provider. It carries no suffix
    /// because, unlike a permission, there is nothing to parameterize: step-up
    /// is satisfied or it is not.
    /// </summary>
    public const string PolicyName = "stepup:";

    /// <summary>The policy name the framework resolves.</summary>
    public string? Policy
    {
        get => PolicyName;
        set => throw new NotSupportedException("The step-up policy name is fixed and cannot be set.");
    }

    /// <summary>Not used. Authentication scheme selection is the host's concern.</summary>
    public string? AuthenticationSchemes { get; set; }

    /// <summary>Not used. The Platform decides access by permission, not by role claim.</summary>
    public string? Roles { get; set; }
}

/// <summary>
/// The requirement a step-up-protected endpoint carries.
/// <para>
/// Defined in the kernel rather than in the Security module so that the
/// Authorization module's policy provider can construct it without referencing
/// Security, and so the handler that evaluates it can live in Security without
/// either module referencing the other (§6.2).
/// </para>
/// </summary>
public sealed class StepUpRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// Marks an authorization failure as "step-up needed" rather than "not
    /// permitted".
    /// <para>
    /// Both answer 403, deliberately — an unauthenticated prober learns nothing
    /// from the status code either way. But the signed-in person needs to be
    /// told which, because the two have opposite remedies: one is fixed by
    /// confirming a second factor, the other by asking an administrator for a
    /// permission. Without this the client is guessing, and the guess is wrong
    /// often enough to send people to ask for access they already hold.
    /// </para>
    /// <para>
    /// The constant lives here, with the requirement, so the handler in Security
    /// can set it and the result handler in the kernel can read it without
    /// either referencing the other (§6.2).
    /// </para>
    /// </summary>
    public const string FailureReason = "step-up-required";

    /// <summary>
    /// The caller has no second factor at all, so there is nothing to confirm.
    /// <para>
    /// <b>A different refusal from a lapsed elevation, and it has to be.</b>
    /// Telling somebody with no enrolment to "verify your second factor and
    /// retry" asks them for a code they cannot produce, and the only way out is
    /// a screen they were not sent to. They would try, fail, and try again.
    /// </para>
    /// <para>
    /// <c>SecurityErrors.MfaRequiredByPolicy</c> was written for exactly this
    /// and was never raised by anything; its own comment described the
    /// distinction as though it existed. This is what makes it true.
    /// </para>
    /// </summary>
    public const string EnrolmentRequiredReason = "mfa-enrolment-required";
}
