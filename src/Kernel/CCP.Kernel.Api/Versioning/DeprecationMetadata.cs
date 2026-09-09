using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace CCP.Kernel.Api.Versioning;

/// <summary>
/// Marks an endpoint as on its way out, and says when it stops.
/// <para>
/// <b>A deprecation nobody is told about is a breaking change with a longer
/// fuse.</b> The Platform is consumed by systems built by other teams who will
/// not read its release notes; the only place they reliably look is the response
/// they already receive. So the warning travels there.
/// </para>
/// <para>
/// Two dates, and they are not the same thing. <see cref="DeprecatedOn"/> is
/// when the endpoint was declared obsolete — from that moment it still works and
/// nobody should build anything new on it. <see cref="SunsetOn"/> is when it
/// stops. Publishing only the first tells a team to move without telling them
/// by when, which is how migrations do not happen.
/// </para>
/// <para>
/// The Platform's versioning policy — what may change inside a version, what
/// forces a new one, and how long a deprecated endpoint is kept — is in
/// <c>docs/api/versioning.md</c>. This type is how the policy reaches a caller.
/// </para>
/// </summary>
/// <param name="DeprecatedOn">When it was declared obsolete.</param>
/// <param name="SunsetOn">
/// When it will stop working. <b>Required.</b> A deprecation with no end date is
/// a note, and notes are ignored.
/// </param>
/// <param name="Replacement">
/// Where to go instead — a path, or a link to the guide. A deprecation that does
/// not say what to use instead leaves the reader with a problem and no next
/// step.
/// </param>
public sealed record DeprecationMetadata(
    DateOnly DeprecatedOn,
    DateOnly SunsetOn,
    string Replacement);

/// <summary>
/// Puts the deprecation on the response, in the headers the standards define.
/// <para>
/// <c>Deprecation</c> and <c>Sunset</c> (RFC 8594) rather than something
/// invented here, because a client library that already understands them costs
/// nobody any work, and a custom header costs everybody some.
/// </para>
/// <para>
/// It runs for every request and does almost nothing: one metadata lookup on the
/// endpoint, and headers only when there is something to say. Nothing is
/// deprecated today, which is exactly when this is worth building — the first
/// deprecation should be a two-line change, not a scramble.
/// </para>
/// </summary>
public sealed class DeprecationHeaderMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // On starting rather than after next(): once the response has begun,
        // headers can no longer be set, and a streamed response begins early.
        context.Response.OnStarting(static state =>
        {
            var httpContext = (HttpContext)state;

            DeprecationMetadata? deprecation =
                httpContext.GetEndpoint()?.Metadata.GetMetadata<DeprecationMetadata>();

            if (deprecation is null)
            {
                return Task.CompletedTask;
            }

            // RFC 8594 wants an HTTP-date. The Platform speaks UTC everywhere
            // and this is a date rather than an instant, so it is rendered at
            // midnight UTC — which is when the endpoint actually stops.
            httpContext.Response.Headers["Deprecation"] = ToHttpDate(deprecation.DeprecatedOn);
            httpContext.Response.Headers["Sunset"] = ToHttpDate(deprecation.SunsetOn);

            // The Link header is where a caller finds out what to do about it.
            httpContext.Response.Headers["Link"] =
                $"<{deprecation.Replacement}>; rel=\"successor-version\"";

            return Task.CompletedTask;
        }, context);

        await next(context);
    }

    private static string ToHttpDate(DateOnly date) =>
        date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            .ToString("R", CultureInfo.InvariantCulture);
}
