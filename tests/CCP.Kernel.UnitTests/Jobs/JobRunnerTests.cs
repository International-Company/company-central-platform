using System.Diagnostics.Metrics;
using CCP.Kernel.Application.Jobs;
using CCP.Kernel.Application.Observability;
using CCP.Kernel.Primitives;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;

namespace CCP.Kernel.UnitTests.Jobs;

/// <summary>
/// The behaviour five background sweeps now share.
/// <para>
/// <b>These exist because the shared behaviour was previously absent from all
/// five.</b> Phase 14 declared <c>ccp.jobs.runs</c> and wrote an alert against
/// it, and no sweep ever called it — the instrument was in a project none of
/// them referenced. Nothing failed, nothing was logged, and the alert sat green
/// on a job that might never have run. Behaviour every job is supposed to share
/// belongs in one class with tests on it.
/// </para>
/// </summary>
public sealed class JobRunnerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 9, 3, 0, 0, TimeSpan.Zero);

    private static (JobRunner Runner, RecordingJournal Journal) Build()
    {
        var journal = new RecordingJournal();

        var runner = new JobRunner(
            journal,
            new PlatformMetrics(new DummyMeterFactory()),
            new FixedClock(Now),
            NullLogger<JobRunner>.Instance);

        return (runner, journal);
    }

    [Fact]
    public async Task A_successful_pass_is_recorded_with_what_it_did()
    {
        (JobRunner runner, RecordingJournal journal) = Build();

        JobOutcome outcome = await runner.RunAsync(
            "documents.purge",
            _ => Task.FromResult<string?>("Purged the content of 3 document(s)."));

        Assert.Equal(JobOutcome.Succeeded, outcome);

        JobRun run = Assert.Single(journal.Runs);
        Assert.Equal("documents.purge", run.Job);
        Assert.Equal(JobOutcome.Succeeded, run.Outcome);
        Assert.Equal("Purged the content of 3 document(s).", run.Summary);
        Assert.Null(run.Error);
        Assert.Equal(Now, run.StartedAt);
    }

    /// <summary>
    /// The point of the whole class. A sweep that throws must not escape, or
    /// the <c>BackgroundService</c> hosting it stops for the life of the
    /// process and the job silently retires until somebody redeploys.
    /// </summary>
    [Fact]
    public async Task A_pass_that_throws_does_not_escape()
    {
        (JobRunner runner, RecordingJournal journal) = Build();

        JobOutcome outcome = await runner.RunAsync(
            "integrations.retention",
            _ => throw new InvalidOperationException("the database went away"));

        Assert.Equal(JobOutcome.Failed, outcome);

        JobRun run = Assert.Single(journal.Runs);
        Assert.Equal(JobOutcome.Failed, run.Outcome);
        Assert.Contains("the database went away", run.Error, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A deployment is not an incident. Counting a cancelled pass as a failure
    /// would make every release produce failures, which trains people to ignore
    /// the release that produces a real one.
    /// </summary>
    [Fact]
    public async Task A_pass_stopped_by_shutdown_is_cancelled_not_failed()
    {
        (JobRunner runner, RecordingJournal journal) = Build();

        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        JobOutcome outcome = await runner.RunAsync(
            "workflow.escalation",
            token => Task.FromException<string?>(new OperationCanceledException(token)),
            stopping.Token);

        Assert.Equal(JobOutcome.Cancelled, outcome);
        Assert.Equal(JobOutcome.Cancelled, Assert.Single(journal.Runs).Outcome);
        Assert.Null(Assert.Single(journal.Runs).Error);
    }

    /// <summary>
    /// A cancellation that is <i>not</i> the host stopping is a real failure —
    /// a timeout inside the job, say — and must not be filed as a tidy
    /// shutdown.
    /// </summary>
    [Fact]
    public async Task A_cancellation_that_is_not_the_shutdown_is_a_failure()
    {
        (JobRunner runner, RecordingJournal journal) = Build();

        using var unrelated = new CancellationTokenSource();
        await unrelated.CancelAsync();

        JobOutcome outcome = await runner.RunAsync(
            "audit.partitions",
            _ => Task.FromException<string?>(new OperationCanceledException(unrelated.Token)),
            CancellationToken.None);

        Assert.Equal(JobOutcome.Failed, outcome);
        Assert.Equal(JobOutcome.Failed, Assert.Single(journal.Runs).Outcome);
    }

    /// <summary>
    /// A sweep with nothing to sweep is the ordinary case. It is still recorded
    /// — the run happening is the fact worth keeping — but it says nothing,
    /// because a history where every line reads "removed 0 rows" is one nobody
    /// reads closely enough to notice the line that does not.
    /// </summary>
    [Fact]
    public async Task A_pass_with_nothing_to_report_is_still_recorded()
    {
        (JobRunner runner, RecordingJournal journal) = Build();

        await runner.RunAsync("integrations.retention", _ => Task.FromResult<string?>(null));

        JobRun run = Assert.Single(journal.Runs);
        Assert.Equal(JobOutcome.Succeeded, run.Outcome);
        Assert.Null(run.Summary);
    }

    /// <summary>
    /// The journal must not be able to fail the job. Refusing to sweep expired
    /// rows because the record of the sweep was unwritable trades the work for
    /// the paperwork.
    /// </summary>
    [Fact]
    public async Task An_unwritable_journal_does_not_fail_the_job()
    {
        var runner = new JobRunner(
            new ThrowingJournal(),
            new PlatformMetrics(new DummyMeterFactory()),
            new FixedClock(Now),
            NullLogger<JobRunner>.Instance);

        bool ran = false;

        JobOutcome outcome = await runner.RunAsync(
            "documents.purge",
            _ =>
            {
                ran = true;

                return Task.FromResult<string?>("done");
            });

        Assert.True(ran);
        Assert.Equal(JobOutcome.Succeeded, outcome);
    }

    /// <summary>
    /// The run cut short by a shutdown is the one most worth recording, so the
    /// journal write must not itself be cancelled by the stopping token.
    /// </summary>
    [Fact]
    public async Task A_cancelled_run_is_still_written_to_the_journal()
    {
        var journal = new CancellationAwareJournal();

        var runner = new JobRunner(
            journal,
            new PlatformMetrics(new DummyMeterFactory()),
            new FixedClock(Now),
            NullLogger<JobRunner>.Instance);

        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        await runner.RunAsync(
            "documents.purge",
            token => Task.FromException<string?>(new OperationCanceledException(token)),
            stopping.Token);

        JobRun run = Assert.Single(journal.Runs);
        Assert.Equal(JobOutcome.Cancelled, run.Outcome);
        Assert.False(journal.SawCancellationRequested);
    }

    [Fact]
    public async Task The_duration_is_measured_rather_than_assumed()
    {
        (JobRunner runner, RecordingJournal journal) = Build();

        await runner.RunAsync(
            "workflow.escalation",
            async token =>
            {
                await Task.Delay(15, token);

                return null;
            });

        // Deliberately loose. The assertion worth making is that something was
        // measured at all — the previous state of the world recorded no
        // duration anywhere, and a test pinning a millisecond count would fail
        // on a loaded build agent and teach everyone to ignore it.
        Assert.True(Assert.Single(journal.Runs).DurationMs > 0);
    }

    [Fact]
    public async Task A_job_must_be_named()
    {
        (JobRunner runner, _) = Build();

        await Assert.ThrowsAsync<ArgumentException>(
            () => runner.RunAsync("  ", _ => Task.FromResult<string?>(null)));
    }

    private sealed class RecordingJournal : IJobJournal
    {
        public List<JobRun> Runs { get; } = [];

        public Task RecordAsync(JobRun run, CancellationToken cancellationToken = default)
        {
            Runs.Add(run);

            return Task.CompletedTask;
        }
    }

    private sealed class CancellationAwareJournal : IJobJournal
    {
        public List<JobRun> Runs { get; } = [];

        public bool SawCancellationRequested { get; private set; }

        public Task RecordAsync(JobRun run, CancellationToken cancellationToken = default)
        {
            SawCancellationRequested = cancellationToken.IsCancellationRequested;
            Runs.Add(run);

            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingJournal : IJobJournal
    {
        public Task RecordAsync(JobRun run, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the journal is unwritable");
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    /// <summary>
    /// A meter factory that produces real, unobserved meters.
    /// <para>
    /// These tests are about the runner's behaviour, not about the metric
    /// values; what matters is that recording one cannot throw.
    /// </para>
    /// </summary>
    private sealed class DummyMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = [];

        public Meter Create(MeterOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            var meter = new Meter(options.Name, options.Version);
            _meters.Add(meter);

            return meter;
        }

        public void Dispose()
        {
            foreach (Meter meter in _meters)
            {
                meter.Dispose();
            }

            _meters.Clear();
        }
    }
}
