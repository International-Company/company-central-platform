using CCP.Kernel.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Authorization.Api;

/// <summary>
/// Builds an authorization policy for each permission, on demand.
/// <para>
/// <b>Policies cannot be registered in advance here</b>, and that is the point.
/// A business application declares permissions the Platform has never heard of
/// (ADR-012), so the set is open-ended and only known at runtime. Encoding the
/// permission into the policy name and constructing the policy when it is first
/// asked for is what lets <c>finance.invoices.approve</c> work without anyone
/// adding a line of Platform code.
/// </para>
/// <para>
/// Built policies are cached, so the cost is paid once per distinct permission
/// per process rather than per request.
/// </para>
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, AuthorizationPolicy> _cache = new(StringComparer.Ordinal);

    private static readonly AuthorizationPolicy StepUpPolicy =
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new StepUpRequirement())
            .Build();

    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (string.Equals(policyName, RequireStepUpAttribute.PolicyName, StringComparison.Ordinal))
        {
            // Step-up carries no parameter, so one policy serves every endpoint
            // that needs it. The requirement type lives in the kernel and the
            // handler that evaluates it lives in the Security module, so neither
            // module has to know about the other (§6.2).
            return Task.FromResult<AuthorizationPolicy?>(StepUpPolicy);
        }

        if (policyName is null || !policyName.StartsWith(RequirePermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            // Not ours. Fall through so ordinary named policies still work.
            return base.GetPolicyAsync(policyName!);
        }

        string permission = policyName[RequirePermissionAttribute.PolicyPrefix.Length..];

        AuthorizationPolicy policy = _cache.GetOrAdd(permission, static p =>
            new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(p))
                .Build());

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}
