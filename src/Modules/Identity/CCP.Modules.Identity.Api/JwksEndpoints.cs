using CCP.Kernel.Api.Security;
using CCP.Modules.Identity.Application.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace CCP.Modules.Identity.Api;

/// <summary>
/// Publishes the token-signing public keys.
/// <para>
/// Anonymous by necessity and by design. A business application must be able to
/// fetch these before it holds any credential, and they are public keys — there
/// is nothing here to protect. Requiring authentication would only make
/// integration harder without making anything safer.
/// </para>
/// </summary>
public static class JwksEndpoints
{
    public static void MapJwksEndpoints(this IEndpointRouteBuilder versionGroup)
    {
        versionGroup.MapGet("/.well-known/jwks.json", (
            [FromServices] IJwksProvider jwksProvider,
            HttpContext context) =>
        {
            JsonWebKeySet keySet = jwksProvider.GetKeySet();

            // Cacheable, but not for long. During a key rotation both the old
            // and new key are published, and a consumer holding a stale copy for
            // hours would reject valid tokens signed with the new one.
            context.Response.Headers.CacheControl = "public, max-age=300";

            return Results.Ok(keySet);
        })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Anonymous)
            .WithTags("Authentication")
            .WithName("Jwks")
            .WithSummary("Public keys for validating Platform access tokens locally.");
    }
}
