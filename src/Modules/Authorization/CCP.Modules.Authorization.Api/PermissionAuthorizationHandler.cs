using System.Security.Claims;
using CCP.Kernel.Api.Security;
using CCP.Modules.Authorization.Application.Abstractions;
using CCP.Modules.Authorization.Domain.Scopes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CCP.Modules.Authorization.Api;

/// <summary>
/// Enforces <see cref="RequirePermissionAttribute"/>.
/// <para>
/// <b>This is what has been missing since Phase 2.</b> Endpoints across Identity
/// and Organization have carried permission declarations that nothing evaluated;
/// this closes that gap. Until it existed, any authenticated user could
/// administer every user and restructure the whole organization.
/// </para>
/// <para>
/// It runs on every authorized request in the company, which is why the resolver
/// behind it caches on a version stamp rather than recomputing a join each time
/// (ADR-007 §14.4).
/// </para>
/// </summary>
public sealed class PermissionAuthorizationHandler(
    IPermissionResolver resolver,
    IAccessDenialRecorder denialRecorder,
    ILogger<PermissionAuthorizationHandler> logger)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (context.User.Identity?.IsAuthenticated != true)
        {
            // Not authenticated. Left unhandled rather than failed, so the
            // framework answers 401 rather than 403 — "who are you" and "you may
            // not" are different answers and clients act on them differently.
            return;
        }

        if (!TryGetUserId(context.User, out Guid userId))
        {
            logger.LogWarning(
                "An authenticated principal carried no usable subject claim. Denying {Permission}.",
                requirement.Permission);

            context.Fail();

            return;
        }

        AccessDecision decision = await resolver.EvaluateAsync(userId, requirement.Permission);

        if (!decision.IsGranted)
        {
            // Recorded, not merely refused. One denial is noise; a burst across
            // many permissions from one caller is someone mapping what they can
            // reach (ARCHITECTURE.md §14.5).
            await denialRecorder.RecordAsync(userId, context.User, requirement.Permission);

            context.Fail();

            return;
        }

        // Carried forward so the endpoint can apply the data filter without
        // resolving a second time. A decision that grants access to *something*
        // still restricts *which* records, and that restriction has to travel.
        //
        // Two shapes: the module's own decision, and the kernel's neutral
        // ScopeFilter that other modules read. Passing this module's type to
        // Identity or Organization would make them reference Authorization's
        // internals, which §6.2 forbids.
        if (context.Resource is HttpContext httpContext)
        {
            httpContext.Items[AccessDecisionKeys.Decision] = decision;
            httpContext.Items[ScopeFilter.HttpContextKey] = ToScopeFilter(decision);
        }

        context.Succeed(requirement);
    }

    /// <summary>
    /// Translates the module's decision into the kernel's neutral shape.
    /// <para>
    /// A deliberate duplication of the enum. Sharing one type would mean every
    /// module that applies a filter referencing Authorization's domain, and the
    /// two are allowed to diverge — the kernel shape is a transport contract,
    /// the domain one is a modelling decision.
    /// </para>
    /// </summary>
    private static ScopeFilter ToScopeFilter(AccessDecision decision)
        => decision.Scope switch
        {
            ScopeType.All => ScopeFilter.Unrestricted,
            ScopeType.Self => ScopeFilter.SelfOnly,
            ScopeType.Unit => new ScopeFilter(ScopeFilterKind.Unit, decision.UnitPathPrefixes),
            ScopeType.UnitAndBelow =>
                new ScopeFilter(ScopeFilterKind.UnitAndBelow, decision.UnitPathPrefixes),
            _ => ScopeFilter.SelfOnly
        };

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        string? subject = principal.FindFirst("sub")?.Value
                       ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out userId);
    }
}

/// <summary>The requirement a permission-protected endpoint carries.</summary>
public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

/// <summary>Keys under which an access decision travels on the request.</summary>
public static class AccessDecisionKeys
{
    /// <summary>
    /// The <see cref="AccessDecision"/> for the permission that admitted this
    /// request, so the endpoint can apply its data filter.
    /// </summary>
    public const string Decision = "ccp.authz.decision";
}

/// <summary>
/// Records a denied authorization decision.
/// <para>
/// A separate seam so the handler stays synchronous in spirit and testable, and
/// so recording can later become a security event without touching enforcement.
/// </para>
/// </summary>
public interface IAccessDenialRecorder
{
    Task RecordAsync(Guid userId, ClaimsPrincipal principal, string permission);
}
