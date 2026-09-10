using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CCP.Kernel.Application.Observability;

/// <summary>
/// The Platform's own instruments (ARCHITECTURE.md §22.3).
/// <para>
/// <b>Only the measurements a person would act on.</b> The framework already
/// emits request rate, duration and error rate for every endpoint, and the
/// database and HTTP client emit theirs; instrumenting those again would produce
/// two numbers for one thing and an argument about which is right.
/// </para>
/// <para>
/// What is here is what the framework cannot know: how many sign-ins failed, how
/// deep the outbox is, whether a background sweep is still finishing. Each one
/// answers a question somebody asks during an incident, and each one has an
/// alert defined against it in <c>docs/deployment/observability.md</c>.
/// </para>
/// <para>
/// <b>It lives in the application layer, and that is not filing.</b> It was in
/// the API layer, which no module's Infrastructure references — so
/// <see cref="BackgroundJobRan"/> could not be called by any of the five
/// background sweeps that were supposed to call it. The instrument existed, the
/// alert was written against it, and nothing on earth emitted it: a permanently
/// green alert on a job that might never have run. Instruments belong where the
/// work they measure can see them.
/// </para>
/// </summary>
public sealed class PlatformMetrics : IDisposable
{
    /// <summary>
    /// The meter name. Also the name an exporter filters on, so it is a
    /// constant rather than a string typed twice.
    /// </summary>
    public const string MeterName = "CompanyCentralPlatform";

    /// <summary>
    /// The activity source for spans the Platform starts itself.
    /// <para>
    /// Most spans come from the automatic instrumentation. This is for the work
    /// that happens outside a request — a sweep, a dispatch — which would
    /// otherwise appear in no trace at all.
    /// </para>
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(MeterName);

    private readonly Meter _meter;
    private readonly Counter<long> _authenticationAttempts;
    private readonly Counter<long> _rateLimitRejections;
    private readonly Counter<long> _permissionDenials;
    private readonly Counter<long> _backgroundJobRuns;
    private readonly Histogram<double> _backgroundJobDuration;
    private readonly Counter<long> _outboxDispatches;

    public PlatformMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        _meter = meterFactory.Create(MeterName);

        // Attempts rather than failures, with an outcome tag. A failure count
        // alone cannot answer "is this a spike or is everybody signing in?" —
        // and that ratio is the thing worth alerting on.
        _authenticationAttempts = _meter.CreateCounter<long>(
            "ccp.authentication.attempts",
            unit: "{attempt}",
            description: "Sign-in attempts, tagged by outcome.");

        _rateLimitRejections = _meter.CreateCounter<long>(
            "ccp.ratelimit.rejections",
            unit: "{rejection}",
            description: "Requests refused by a rate limiter, tagged by policy.");

        // A single denial is noise; a burst across many permissions from one
        // caller is somebody mapping what they can reach (§14.5).
        _permissionDenials = _meter.CreateCounter<long>(
            "ccp.authorization.denials",
            unit: "{denial}",
            description: "Permission checks that refused, tagged by permission.");

        _backgroundJobRuns = _meter.CreateCounter<long>(
            "ccp.jobs.runs",
            unit: "{run}",
            description: "Background job executions, tagged by job and outcome.");

        _backgroundJobDuration = _meter.CreateHistogram<double>(
            "ccp.jobs.duration",
            unit: "ms",
            description: "How long a background job took.");

        _outboxDispatches = _meter.CreateCounter<long>(
            "ccp.outbox.dispatches",
            unit: "{message}",
            description: "Integration events dispatched, tagged by outcome.");
    }

    public void AuthenticationAttempted(bool succeeded) =>
        _authenticationAttempts.Add(1, Tag("outcome", succeeded ? "success" : "failure"));

    public void RateLimitRejected(string policy) =>
        _rateLimitRejections.Add(1, Tag("policy", policy));

    /// <summary>
    /// A refused permission check.
    /// <para>
    /// Tagged by the permission, not by the caller. A caller id would be a
    /// person's identifier in a metrics backend, which is a place personal data
    /// has no business being — and the permission is what says whether a burst
    /// is somebody probing.
    /// </para>
    /// </summary>
    public void PermissionDenied(string permission) =>
        _permissionDenials.Add(1, Tag("permission", permission));

    public void BackgroundJobRan(string job, bool succeeded, double durationMs)
    {
        _backgroundJobRuns.Add(
            1,
            new KeyValuePair<string, object?>("job", job),
            new KeyValuePair<string, object?>("outcome", succeeded ? "success" : "failure"));

        _backgroundJobDuration.Record(durationMs, Tag("job", job));
    }

    public void OutboxDispatched(bool succeeded) =>
        _outboxDispatches.Add(1, Tag("outcome", succeeded ? "success" : "failure"));

    private static KeyValuePair<string, object?> Tag(string name, string value) =>
        new(name, value);

    public void Dispose() => _meter.Dispose();
}
