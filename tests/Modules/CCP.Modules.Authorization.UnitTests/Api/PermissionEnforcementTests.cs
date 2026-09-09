using System.Security.Claims;
using CCP.Kernel.Api.Security;
using CCP.Modules.Authorization.Api;
using CCP.Modules.Authorization.Application.Abstractions;
using CCP.Modules.Authorization.Domain.Scopes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Authorization.UnitTests.Api;

/// <summary>
/// Enforcement itself.
/// <para>
/// This is the piece that was missing from Phase 2 until now: endpoints declared
/// permissions and nothing evaluated them. These tests assert that the handler
/// actually refuses, actually distinguishes "not authenticated" from "not
/// permitted", and actually carries the data filter forward.
/// </para>
/// </summary>
public sealed class PermissionEnforcementTests
{
    private const string Permission = "platform.users.view";

    private static readonly Guid UserId = Guid.CreateVersion7();

    // -----------------------------------------------------------------------
    // The policy provider
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ThePolicyProvider_BuildsAPolicyForAnyPermission()
    {
        // Policies cannot be registered in advance: a business application
        // declares permissions the Platform has never heard of (ADR-012), so
        // the set is only known at runtime.
        var provider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()));

        AuthorizationPolicy? policy = await provider.GetPolicyAsync(
            "permission:finance.invoices.approve");

        Assert.NotNull(policy);

        PermissionRequirement requirement =
            Assert.Single(policy.Requirements.OfType<PermissionRequirement>());

        Assert.Equal("finance.invoices.approve", requirement.Permission);
    }

    [Fact]
    public async Task ThePolicyProvider_RequiresAuthentication()
    {
        var provider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()));

        AuthorizationPolicy policy = (await provider.GetPolicyAsync($"permission:{Permission}"))!;

        Assert.Contains(policy.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public async Task ThePolicyProvider_IgnoresPolicyNamesThatAreNotOurs()
    {
        // Ordinary named policies must keep working.
        var provider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()));

        Assert.Null(await provider.GetPolicyAsync("some-other-policy"));
    }

    [Fact]
    public void TheAttributeProducesTheExpectedPolicyName()
    {
        // The attribute and the provider have to agree on the encoding, or every
        // permission check silently falls through to the default policy.
        var attribute = new RequirePermissionAttribute("platform.roles.assign");

        Assert.Equal("permission:platform.roles.assign", attribute.Policy);
    }

    // -----------------------------------------------------------------------
    // The handler
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AnUnauthenticatedCaller_IsLeftUnhandled()
    {
        // Deliberately not failed. Leaving it unhandled makes the framework
        // answer 401; failing it would answer 403. "Who are you" and "you may
        // not" are different answers and clients act on them differently.
        var handler = CreateHandler(new StubResolver(granted: false));

        AuthorizationHandlerContext context = ContextFor(new ClaimsPrincipal(new ClaimsIdentity()));

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.False(context.HasFailed);
    }

    [Fact]
    public async Task AnAuthenticatedCallerWithoutThePermission_IsRefused()
    {
        var handler = CreateHandler(new StubResolver(granted: false));

        AuthorizationHandlerContext context = ContextFor(AuthenticatedUser(UserId));

        await handler.HandleAsync(context);

        Assert.True(context.HasFailed);
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task ARefusalIsRecorded()
    {
        // One denial is noise; a burst across many permissions from one caller
        // is reconnaissance (ARCHITECTURE.md §14.5).
        var recorder = new RecordingDenialRecorder();
        var handler = CreateHandler(new StubResolver(granted: false), recorder);

        await handler.HandleAsync(ContextFor(AuthenticatedUser(UserId)));

        Assert.Single(recorder.Denials);
        Assert.Equal((UserId, Permission), recorder.Denials[0]);
    }

    [Fact]
    public async Task AnAuthenticatedCallerWithThePermission_IsAdmitted()
    {
        var handler = CreateHandler(new StubResolver(granted: true));

        AuthorizationHandlerContext context = ContextFor(AuthenticatedUser(UserId));

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        Assert.False(context.HasFailed);
    }

    [Fact]
    public async Task AnAdmittedRequestCarriesTheDataFilterForward()
    {
        // Being admitted says the caller may do this to *something*. Which
        // records is a separate restriction, and it has to travel — otherwise
        // the endpoint would have to resolve permissions a second time, and
        // eventually one would forget.
        var httpContext = new DefaultHttpContext();

        var handler = CreateHandler(new StubResolver(
            granted: true,
            decision: AccessDecision.GrantedForUnits(ScopeType.UnitAndBelow, ["/hq/finance/"])));

        var context = new AuthorizationHandlerContext(
            [new PermissionRequirement(Permission)], AuthenticatedUser(UserId), httpContext);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);

        var carried = Assert.IsType<AccessDecision>(httpContext.Items[AccessDecisionKeys.Decision]);

        Assert.Equal(ScopeType.UnitAndBelow, carried.Scope);
        Assert.Equal(["/hq/finance/"], carried.UnitPathPrefixes);
    }

    [Fact]
    public async Task APrincipalWithNoUsableSubject_IsRefused()
    {
        // Authenticated but carrying no subject claim is a broken token, not a
        // permitted caller. Failing closed is the only safe answer.
        var handler = CreateHandler(new StubResolver(granted: true));

        var identity = new ClaimsIdentity(authenticationType: "Test");
        identity.AddClaim(new Claim("username", "ahmad"));

        AuthorizationHandlerContext context = ContextFor(new ClaimsPrincipal(identity));

        await handler.HandleAsync(context);

        Assert.True(context.HasFailed);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static PermissionAuthorizationHandler CreateHandler(
        StubResolver resolver,
        IAccessDenialRecorder? recorder = null)
        => new(resolver,
               recorder ?? new RecordingDenialRecorder(),
               NullLogger<PermissionAuthorizationHandler>.Instance);

    private static AuthorizationHandlerContext ContextFor(ClaimsPrincipal principal)
        => new([new PermissionRequirement(Permission)], principal, resource: null);

    private static ClaimsPrincipal AuthenticatedUser(Guid userId)
    {
        var identity = new ClaimsIdentity(authenticationType: "Test");
        identity.AddClaim(new Claim("sub", userId.ToString()));
        identity.AddClaim(new Claim("username", "ahmad"));

        return new ClaimsPrincipal(identity);
    }

    private sealed class StubResolver(bool granted, AccessDecision? decision = null) : IPermissionResolver
    {
        public Task<EffectivePermissions> GetEffectivePermissionsAsync(
            Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(
                EffectivePermissions.None(PermissionSubject.ForUser(userId), 1));

        public Task<string?> GetUserUnitPathAsync(
            Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<AccessDecision> EvaluateAsync(
            Guid userId, string permissionName, CancellationToken cancellationToken = default)
            => Task.FromResult(Answer());

        public Task<EffectivePermissions> GetEffectivePermissionsForApplicationAsync(
            Guid applicationId, CancellationToken cancellationToken = default)
            => Task.FromResult(
                EffectivePermissions.None(PermissionSubject.ForApplication(applicationId), 1));

        public Task<AccessDecision> EvaluateForApplicationAsync(
            Guid applicationId, string permissionName, CancellationToken cancellationToken = default)
            => Task.FromResult(Answer());

        public Task<AccessDecision> EvaluateDelegatedAsync(
            Guid applicationId, Guid userId, string permissionName,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Answer());

        private AccessDecision Answer() => granted
            ? decision ?? AccessDecision.GrantedForAll()
            : AccessDecision.Denied;
    }

    private sealed class RecordingDenialRecorder : IAccessDenialRecorder
    {
        public List<(Guid UserId, string Permission)> Denials { get; } = [];

        public Task RecordAsync(Guid userId, ClaimsPrincipal principal, string permission)
        {
            Denials.Add((userId, permission));

            return Task.CompletedTask;
        }
    }
}
