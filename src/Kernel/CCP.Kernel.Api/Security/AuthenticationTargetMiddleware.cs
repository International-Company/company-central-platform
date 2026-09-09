using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace CCP.Kernel.Api.Security;

/// <summary>
/// Works out which account a sign-in attempt is aimed at, before the rate
/// limiter runs.
/// <para>
/// <b>Why this exists.</b> Rate limiting by source address is the obvious design
/// and it collapses behind NAT: every employee in one office shares one public
/// address, so a ten-a-minute budget becomes ten sign-ins a minute for the whole
/// company. Limiting by the <i>account being attacked</i> instead is what makes
/// the budget mean something — but the account is in the request body, and the
/// limiter runs before any endpoint has read it.
/// </para>
/// <para>
/// So the body is buffered here and the username lifted out. It is a small cost
/// paid on a handful of anonymous endpoints with tiny payloads, and it is what
/// lets fifty colleagues sign in at nine o'clock while an attacker still gets ten
/// attempts a minute against any one account.
/// </para>
/// <para>
/// A parse failure is not an error. A malformed body is refused later, by
/// validation, with a proper message; the limiter simply falls back to the
/// address, which is the safe direction to fail in.
/// </para>
/// </summary>
public sealed class AuthenticationTargetMiddleware(RequestDelegate next)
{
    /// <summary>Where the extracted account name travels on the request.</summary>
    public const string HttpContextKey = "ccp.auth.target";

    /// <summary>Where the extracted client id travels on the request.</summary>
    public const string ClientIdKey = "ccp.auth.client-id";

    /// <summary>The machine token endpoint, whose body names a client rather than a person.</summary>
    private const string TokenPath = "/oauth/token";

    /// <summary>
    /// Paths whose body names an account. Matched as a suffix so the version
    /// prefix does not have to be repeated here.
    /// </summary>
    private static readonly string[] AccountBearingPaths =
    [
        "/auth/login",
        "/auth/password/forgot",
        "/auth/password/reset"
    ];

    /// <summary>
    /// The most body to read. A sign-in payload is a few hundred bytes; anything
    /// larger is not one, and buffering it would hand an attacker a way to make
    /// the limiter itself expensive.
    /// </summary>
    private const int MaxBufferedBytes = 4 * 1024;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (ShouldInspect(context))
        {
            context.Items[HttpContextKey] = await ReadUsernameAsync(context);
        }
        else if (IsTokenRequest(context))
        {
            // The same trick for machines. The client id is public — safe to
            // read, safe to log, useless alone — so keying a limit on it before
            // anything is authenticated gives away nothing, and it is what stops
            // one badly written integration starving every other application
            // behind the same office egress.
            context.Items[ClientIdKey] = ReadClientId(context);
        }

        await next(context);
    }

    private static bool IsTokenRequest(HttpContext context)
        => HttpMethods.IsPost(context.Request.Method)
        && context.Request.HasFormContentType
        && context.Request.Path.Value?.EndsWith(TokenPath, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// The client id from a form-encoded token request, or null.
    /// <para>
    /// Reading the form here parses it once and caches it on the request, so the
    /// endpoint that reads it afterwards costs nothing extra. A body that will
    /// not parse falls back to an address-keyed limit, which is the safe
    /// direction.
    /// </para>
    /// </summary>
    private static string? ReadClientId(HttpContext context)
    {
        try
        {
            return context.Request.Form["client_id"].ToString() is { Length: > 0 } clientId
                ? clientId
                : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static bool ShouldInspect(HttpContext context)
        => HttpMethods.IsPost(context.Request.Method)
        && context.Request.ContentLength is > 0 and <= MaxBufferedBytes
        && AccountBearingPaths.Any(p =>
            context.Request.Path.Value?.EndsWith(p, StringComparison.OrdinalIgnoreCase) == true);

    private static async Task<string?> ReadUsernameAsync(HttpContext context)
    {
        try
        {
            // Buffering so the endpoint can still read the body afterwards.
            // Without this the model binder would find an already-consumed
            // stream and every sign-in would fail.
            context.Request.EnableBuffering();

            using var document = await JsonDocument.ParseAsync(
                context.Request.Body, cancellationToken: context.RequestAborted);

            context.Request.Body.Position = 0;

            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                return null;
            }

            foreach (string name in (string[])["username", "userName", "email"])
            {
                if (document.RootElement.TryGetProperty(name, out JsonElement value)
                    && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } username)
                {
                    // Lower-cased to match how usernames are stored. Otherwise
                    // "Amira" and "amira" would receive separate budgets, and an
                    // attacker would get one per spelling.
                    return username.Trim().ToLowerInvariant();
                }
            }

            return null;
        }
        catch (JsonException)
        {
            // Malformed body. Validation will refuse it with a proper message;
            // the limiter falls back to the address, which is the safe direction.
            RewindQuietly(context);

            return null;
        }
        catch (InvalidOperationException)
        {
            RewindQuietly(context);

            return null;
        }
    }

    private static void RewindQuietly(HttpContext context)
    {
        if (context.Request.Body.CanSeek)
        {
            context.Request.Body.Position = 0;
        }
    }
}
