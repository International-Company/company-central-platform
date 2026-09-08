using Microsoft.AspNetCore.Authorization;

namespace CCP.Kernel.Api.Security;

/// <summary>
/// Declares the permission an endpoint requires, in the
/// <c>&lt;application&gt;.&lt;resource&gt;.&lt;action&gt;</c> form (ADR-007).
/// <para>
/// Every endpoint carries either this, <see cref="AllowAnonymousAttribute"/>, or
/// <see cref="AuthenticatedUserOnlyAttribute"/>. An architecture test fails the
/// build for any endpoint that carries none, so an endpoint cannot be left
/// unprotected by oversight.
/// </para>
/// <para>
/// It implements <see cref="IAuthorizeData"/>, which is what makes the framework
/// enforce it: the authorization middleware finds it in endpoint metadata and
/// resolves <see cref="Policy"/> through the policy provider, which turns the
/// name back into a permission requirement. Before Phase 4 this attribute
/// declared intent and enforced nothing — the policy provider and its handler
/// are what closed that gap.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : Attribute, IAuthorizeData
{
    /// <summary>
    /// Prefix marking a policy name as a permission requirement. The policy
    /// provider recognises it and builds the requirement from the remainder.
    /// </summary>
    public const string PolicyPrefix = "permission:";

    public RequirePermissionAttribute(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        Permission = permission;
    }

    /// <summary>The permission required, e.g. <c>platform.users.create</c>.</summary>
    public string Permission { get; }

    /// <summary>
    /// The policy name the framework resolves. Encoding the permission into the
    /// name means no policy has to be registered in advance — which matters
    /// because business applications declare permissions the Platform has never
    /// heard of (ADR-012).
    /// </summary>
    public string? Policy
    {
        get => PolicyPrefix + Permission;
        set => throw new NotSupportedException(
            "The policy name is derived from the permission and cannot be set.");
    }

    /// <summary>Not used. Authentication scheme selection is the host's concern.</summary>
    public string? AuthenticationSchemes { get; set; }

    /// <summary>
    /// Not used. Roles here would be ASP.NET Core role claims, which the
    /// Platform deliberately does not use — access is decided by permission, and
    /// a second parallel mechanism would be a second place to get it wrong.
    /// </summary>
    public string? Roles { get; set; }
}
