namespace CCP.Kernel.Infrastructure.Jobs;

/// <summary>Tuning for the job history. Bound from configuration.</summary>
public sealed class JobJournalOptions
{
    public const string SectionName = "JobJournal";

    /// <summary>
    /// How long a run is kept.
    /// <para>
    /// Thirty days, because the question this table exists to answer is asked
    /// about last night and occasionally about last month — "it has been failing
    /// since the release three weeks ago" is a real sentence, and "since March"
    /// is a question for the metrics backend, which keeps rates far more
    /// cheaply than rows.
    /// </para>
    /// </summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// How often the journal prunes itself.
    /// <para>
    /// The journal prunes rather than a sixth sweep doing it, because a job
    /// history that needed its own background job to stay bounded would be the
    /// one job whose failure nothing records.
    /// </para>
    /// </summary>
    public TimeSpan PruneInterval { get; set; } = TimeSpan.FromHours(6);
}
