using System.Diagnostics;
using CCP.Kernel.Application.Observability;
using CCP.Kernel.Primitives;
using Microsoft.Extensions.Logging;

namespace CCP.Kernel.Application.Jobs;

/// <summary>
/// Runs one pass of a background job and records what happened.
/// <para>
/// <b>One class, so five sweeps behave the same when something goes wrong.</b>
/// Before this, each sweep had its own <c>try</c>/<c>catch</c> around its own
/// body, and they had already drifted: some logged the exception, none of them
/// timed the pass, none reported an outcome, and the <c>ccp.jobs.runs</c>
/// instrument that Phase 14 declared and wrote an alert against was called by
/// nothing at all. That last one is the argument for this class. Behaviour every
/// job is supposed to share, written once per job, is behaviour that is missing
/// from at least one of them.
/// </para>
/// <para>
/// <b>A failing pass must not stop the timer.</b> The exception is caught and
/// recorded, never rethrown, because a <see cref="Microsoft.Extensions.Hosting.BackgroundService"/>
/// whose <c>ExecuteAsync</c> throws stops for the lifetime of the process — one
/// bad pass would silently retire the sweep until somebody redeployed. The next
/// tick finds the same work and tries again, which is what should happen.
/// </para>
/// </summary>
public sealed class JobRunner(
    IJobJournal journal,
    PlatformMetrics metrics,
    IClock clock,
    ILogger<JobRunner> logger)
{
    /// <summary>
    /// Runs <paramref name="work"/> once, under a span, timed, and recorded.
    /// </summary>
    /// <param name="job">
    /// The job's stable name, e.g. <c>integrations.retention</c>. Written down
    /// rather than taken from a type name, so a refactor cannot quietly start a
    /// new job history and leave the old one looking as though it stopped.
    /// </param>
    /// <param name="work">
    /// The pass. Returns a one-line summary of what it did, or <c>null</c> if
    /// there is nothing worth saying — a sweep that found nothing to sweep is
    /// the normal case and does not need a sentence about it.
    /// </param>
    /// <param name="cancellationToken">The host's stopping token.</param>
    public async Task<JobOutcome> RunAsync(
        string job,
        Func<CancellationToken, Task<string?>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(job);
        ArgumentNullException.ThrowIfNull(work);

        // Background work belongs to no request, so without this it appears in
        // no trace at all — and a sweep that takes four minutes is invisible
        // next to the requests it is slowing down.
        using Activity? activity = PlatformMetrics.ActivitySource.StartActivity(
            $"job {job}",
            ActivityKind.Internal);

        DateTimeOffset startedAt = clock.UtcNow;
        long startedTicks = Stopwatch.GetTimestamp();

        JobOutcome outcome;
        string? summary = null;
        string? error = null;

        try
        {
            summary = await work(cancellationToken);
            outcome = JobOutcome.Succeeded;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping. Not a failure, and recorded as itself so a
            // deployment does not read as an outage.
            outcome = JobOutcome.Cancelled;
        }
#pragma warning disable CA1031 // A job must survive anything its body throws; see the class remarks.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            outcome = JobOutcome.Failed;
            error = $"{exception.GetType().Name}: {exception.Message}";

            logger.LogError(exception, "Background job {Job} failed.", job);
        }

        double durationMs = Stopwatch.GetElapsedTime(startedTicks).TotalMilliseconds;

        activity?.SetTag("ccp.job", job);
        activity?.SetTag("ccp.job.outcome", outcome.ToString());

        if (outcome == JobOutcome.Failed)
        {
            activity?.SetStatus(ActivityStatusCode.Error, error);
        }

        metrics.BackgroundJobRan(job, outcome == JobOutcome.Succeeded, durationMs);

        await RecordAsync(new JobRun(job, startedAt, durationMs, outcome, summary, error));

        return outcome;
    }

    /// <summary>
    /// Writes the run to the journal, swallowing anything that goes wrong.
    /// <para>
    /// The journal contract says implementations must not throw, and this is
    /// the belt to that pair of braces: an unwritable journal must not turn a
    /// successful sweep into a failed one, and must not be silent about it
    /// either.
    /// </para>
    /// <para>
    /// It takes no cancellation token, and that is the point. The run that most
    /// needs recording is the one cut short by a shutdown, and passing the
    /// stopping token here would cancel the write that records exactly that.
    /// </para>
    /// </summary>
    private async Task RecordAsync(JobRun run)
    {
        try
        {
            await journal.RecordAsync(run, CancellationToken.None);
        }
#pragma warning disable CA1031 // Recording the run must not be able to fail the run.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            logger.LogError(
                exception,
                "The job journal could not record a run of {Job}. The job itself {Outcome}.",
                run.Job,
                run.Outcome);
        }
    }
}
