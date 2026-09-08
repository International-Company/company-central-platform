using Microsoft.AspNetCore.Http;

namespace CCP.Kernel.Api.Security;

/// <summary>
/// Applies the response security headers required by ARCHITECTURE.md §12.9.
/// <para>
/// These are cheap, and each closes a real attack: content sniffing, framing,
/// referrer leakage, and access to device features the API never needs.
/// </para>
/// <para>
/// The headers are registered through <see cref="HttpResponse.OnStarting(Func{Task})"/>
/// rather than being written on the way in. Setting them immediately would work
/// for a normal response, but any downstream middleware that resets the
/// response — the exception boundary calls <c>Response.Clear()</c> before
/// writing a Problem Details body — would silently strip them, leaving error
/// responses unprotected. Registering a callback means they are applied at the
/// last possible moment, after any such reset, so <b>every</b> response carries
/// them regardless of how it was produced.
/// </para>
/// <para>
/// This was found in Phase 1 by checking the headers on a 500 response rather
/// than assuming they matched a 200.
/// </para>
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            IHeaderDictionary headers = ((HttpContext)state).Response.Headers;

            // Never let a browser guess a content type. Combined with returning
            // application/json, this blocks a class of XSS via uploaded content.
            headers["X-Content-Type-Options"] = "nosniff";

            // The API is not a page and must never be framed.
            headers["X-Frame-Options"] = "DENY";

            // Do not leak the full URL (which may contain identifiers) off-origin.
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // The API needs none of these device capabilities.
            headers["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), "
                                          + "gyroscope=(), magnetometer=(), microphone=(), "
                                          + "payment=(), usb=()";

            // A JSON API renders nothing, so the strictest possible policy applies.
            // The frontend serves its own, looser policy for the pages it renders.
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; "
                                               + "base-uri 'none'; form-action 'none'";

            // Remove headers that advertise the stack to an attacker.
            headers.Remove("Server");
            headers.Remove("X-Powered-By");

            return Task.CompletedTask;
        }, context);

        return next(context);
    }
}
