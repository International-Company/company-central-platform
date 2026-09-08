using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace CCP.Kernel.Api.Security;

/// <summary>
/// Per-endpoint rate limiting (ARCHITECTURE.md §12.6).
/// <para>
/// A single global limit is not rate limiting for an authentication endpoint. A
/// budget generous enough for browsing a user list is enormous for password
/// guessing, and the two cannot share one number. These policies exist so that
/// each class of endpoint gets a limit shaped by what abusing it would look
/// like.
/// </para>
/// <para>
/// <b>Sign-in needs protection in two directions, and they are handled by two
/// different mechanisms.</b> One address working through the directory is
/// stopped here, by an address-partitioned limit. Many addresses attacking one
/// account is stopped by that account's own progressive lockout, built in
/// Phase 2 — which is also why the lockout is a growing delay rather than a hard
/// lock, so it slows an attacker without handing them a way to lock out anyone
/// they can name.
/// </para>
/// <para>
/// Rate limiting alone would leave the second direction open, and lockout alone
/// would leave the first. Neither is sufficient by itself, and it is worth being
/// precise about which does which — they fail differently and are tuned
/// separately.
/// </para>
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Sign-in, refresh, password reset. The endpoints an attacker attacks.
    /// </summary>
    public const string Authentication = "auth";

    /// <summary>
    /// Anonymous endpoints that are not credential-related. Looser than
    /// authentication, tighter than authenticated traffic.
    /// </summary>
    public const string Anonymous = "anonymous";

    /// <summary>Authenticated writes.</summary>
    public const string Write = "write";

    /// <summary>Authenticated reads. The most generous.</summary>
    public const string Read = "read";

    /// <summary>
    /// The window every policy measures against. One minute, and one constant:
    /// the <c>Retry-After</c> fallback is only honest while it matches the
    /// window the limiter actually uses.
    /// </summary>
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>Registers every policy and the rejection behaviour.</summary>
    /// <param name="options">The framework's limiter options.</param>
    /// <param name="limits">
    /// The per-minute budgets. Supplied rather than hard-coded so a deployment
    /// can tune them without a code change — see <see cref="RateLimitOptions"/>.
    /// </param>
    public static RateLimiterOptions AddPlatformPolicies(
        this RateLimiterOptions options,
        RateLimitOptions limits)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(limits);

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        // Tells a well-behaved client when to come back, and gives an honest
        // client a way to back off rather than hammer. An attacker ignores it,
        // which costs nothing.
        //
        // The limiter does not always supply RetryAfter metadata — a sliding
        // window rejects without one — so the window length is the fallback.
        // Without it the header simply vanished on exactly the responses that
        // needed it, which an integration test caught. A conservative answer is
        // far better than none: a client with no guidance retries immediately
        // and is refused again.
        options.OnRejected = static (context, cancellationToken) =>
        {
            TimeSpan retryAfter =
                context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan fromLease)
                    ? fromLease
                    : Window;

            context.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

            return ValueTask.CompletedTask;
        };

        options.AddPolicy(Authentication, PartitionAuthentication(limits.Authentication));
        options.AddPolicy(Anonymous, PartitionAnonymous(limits.Anonymous));
        options.AddPolicy(Write, PartitionByIdentity(limits.Write, Window));
        options.AddPolicy(Read, PartitionByIdentity(limits.Read, Window));

        return options;
    }

    /// <summary>
    /// The authentication limiter, partitioned by <b>the account being
    /// attacked</b> rather than by where the attempt came from.
    /// <para>
    /// Ten attempts a minute against any one account. That is far more than a
    /// person mistyping a password and far less than useful for guessing, and —
    /// crucially — it holds however many addresses an attacker spreads across.
    /// </para>
    /// <para>
    /// <b>Partitioning by address was the obvious design and it collapses behind
    /// NAT.</b> Every employee in one office shares one public address, so a
    /// per-address budget is really "ten sign-ins a minute for the whole
    /// company": the eleventh person arriving on Sunday morning is refused, and
    /// nothing they can do helps. Keying on the target instead means fifty
    /// colleagues signing in at nine o'clock occupy fifty separate budgets.
    /// </para>
    /// <para>
    /// The source is not thereby unguarded. A separate address-partitioned limit
    /// covers the other direction — one machine spraying many accounts — and the
    /// account's own progressive lockout is the third layer. Each stops
    /// something the others do not; see docs/security/rate-limiting.md §3.
    /// </para>
    /// <para>
    /// The username is falls back to the address when it cannot be determined,
    /// which is the safe direction: an unparseable body is limited as before
    /// rather than escaping the limiter.
    /// </para>
    /// <para>
    /// A sliding window rather than a fixed one, deliberately: a fixed window
    /// lets an attacker fire a full budget at the end of one window and another
    /// immediately at the start of the next, doubling the real rate at the
    /// boundary.
    /// </para>
    /// </summary>
    private static Func<HttpContext, RateLimitPartition<string>> PartitionAuthentication(int permitLimit)
        => context => RateLimitPartition.GetSlidingWindowLimiter(
            AuthenticationTarget(context),
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = Window,
                SegmentsPerWindow = 6,

                // No queue. Holding an authentication attempt to serve it later
                // is pointless — the caller has already given up or retried —
                // and a queue is itself a resource an attacker can exhaust.
                QueueLimit = 0
            });

    private static Func<HttpContext, RateLimitPartition<string>> PartitionAnonymous(int permitLimit)
        => context => RateLimitPartition.GetSlidingWindowLimiter(
            $"anon:{ClientAddress(context)}",
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = Window,
                SegmentsPerWindow = 6,
                QueueLimit = 0
            });

    /// <summary>
    /// Partitions by authenticated identity, falling back to address.
    /// <para>
    /// Per-identity rather than per-address so that one noisy user cannot
    /// consume the allowance of everyone else behind the same office NAT — which
    /// in a company is most people.
    /// </para>
    /// </summary>
    private static Func<HttpContext, RateLimitPartition<string>> PartitionByIdentity(
        int permitLimit,
        TimeSpan window)
        => context =>
        {
            string? subject = context.User.FindFirst("sub")?.Value;

            string key = subject is not null
                ? $"user:{subject}"
                : $"ip:{ClientAddress(context)}";

            return RateLimitPartition.GetSlidingWindowLimiter(
                key,
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = window,
                    SegmentsPerWindow = 6,
                    QueueLimit = 0
                });
        };

    /// <summary>
    /// The partition key for an authentication attempt: the account being
    /// targeted, or the source address when no account can be read.
    /// </summary>
    private static string AuthenticationTarget(HttpContext context)
        => context.Items.TryGetValue(AuthenticationTargetMiddleware.HttpContextKey, out object? value)
            && value is string username
                ? $"auth:user:{username}"
                : $"auth:ip:{ClientAddress(context)}";

    /// <summary>
    /// The other direction: one machine working through many accounts.
    /// <para>
    /// Returned as a chained global limiter rather than an endpoint policy,
    /// because an endpoint carries one policy and the authentication endpoints
    /// already carry the per-account one. Both have to apply — per-account alone
    /// would let a single machine try ten attempts against each of a thousand
    /// names, which is exactly what credential stuffing is.
    /// </para>
    /// <para>
    /// Generous enough for an office behind one address, far below useful for
    /// spraying. Every other path is unlimited here and left to its own policy.
    /// </para>
    /// </summary>
    public static PartitionedRateLimiter<HttpContext> CreateAuthenticationSourceLimiter(int permitLimit)
        => PartitionedRateLimiter.Create<HttpContext, string>(context =>
            IsAuthenticationPath(context)
                ? RateLimitPartition.GetSlidingWindowLimiter(
                    $"auth-source:{ClientAddress(context)}",
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = Window,
                        SegmentsPerWindow = 6,
                        QueueLimit = 0
                    })
                : RateLimitPartition.GetNoLimiter<string>("unlimited"));

    private static bool IsAuthenticationPath(HttpContext context)
        => context.Request.Path.Value is { } path
        && path.Contains("/auth/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The caller's address, as the proxy reported it.
    /// <para>
    /// <c>UseForwardedHeaders</c> has already replaced <c>RemoteIpAddress</c>
    /// with the forwarded value, and it only does so for proxies it trusts. That
    /// matters: taking <c>X-Forwarded-For</c> at face value would let anyone
    /// bypass every limit here by varying a header.
    /// </para>
    /// </summary>
    private static string ClientAddress(HttpContext context)
        => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
