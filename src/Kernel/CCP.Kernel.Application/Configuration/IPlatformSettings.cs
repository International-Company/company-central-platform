namespace CCP.Kernel.Application.Configuration;

/// <summary>
/// The seam every module reads a changeable setting through.
/// <para>
/// <b>It lives in the kernel, not in the Configuration module.</b> The Kernel's
/// own outbox, and the sweeps in Documents, Integrations and Workflow, all have
/// numbers that ought to be changeable without a deployment — and none of them
/// may reference the Configuration module (ARCHITECTURE.md §6.2), the kernel
/// least of all. Putting the contract here and the implementation there is what
/// lets every module read a setting without any of them knowing that a
/// Configuration module exists. The same shape as <c>IAuditTrail</c> and
/// <c>IJobJournal</c>.
/// </para>
/// <para>
/// <b>Why this exists at all.</b> Phase 13 built settings that are declared,
/// typed, scoped, audited and version-stamped, and then nothing used them: every
/// retention period and interval written since Phase 9 stayed an
/// <c>appsettings</c> value needing a deployment to change, which is precisely
/// the problem the module was built to solve. It sat finished and unclaimed for
/// seven phases.
/// </para>
/// <para>
/// <b>Deliberately read-only, and deliberately narrow.</b> Writing a setting is
/// an audited administrative act with an actor and a reason; a background sweep
/// has none of those and has no business doing it. Three getters, each taking
/// the fallback the caller would otherwise have hard-coded.
/// </para>
/// </summary>
public interface IPlatformSettings
{
    /// <summary>
    /// A duration setting, or <paramref name="fallback"/> when it is not
    /// declared, not set, or unreadable.
    /// <para>
    /// The fallback is required rather than optional on purpose. A caller that
    /// could omit it would be a caller whose behaviour changes when the database
    /// is briefly unreachable, and a retention sweep that read zero would delete
    /// everything.
    /// </para>
    /// </summary>
    Task<TimeSpan> GetDurationAsync(
        string key, TimeSpan fallback, CancellationToken cancellationToken = default);

    Task<int> GetIntegerAsync(string key, int fallback, CancellationToken cancellationToken = default);

    Task<bool> GetBooleanAsync(string key, bool fallback, CancellationToken cancellationToken = default);
}

/// <summary>
/// The settings source when no Configuration module is registered.
/// <para>
/// Every caller gets its own fallback, which is the value it shipped with — so a
/// host without Configuration behaves exactly as the Platform did before any of
/// this existed. Registered by the kernel so that is the default rather than a
/// null reference.
/// </para>
/// </summary>
public sealed class DefaultPlatformSettings : IPlatformSettings
{
    public Task<TimeSpan> GetDurationAsync(
        string key, TimeSpan fallback, CancellationToken cancellationToken = default)
        => Task.FromResult(fallback);

    public Task<int> GetIntegerAsync(
        string key, int fallback, CancellationToken cancellationToken = default)
        => Task.FromResult(fallback);

    public Task<bool> GetBooleanAsync(
        string key, bool fallback, CancellationToken cancellationToken = default)
        => Task.FromResult(fallback);
}

/// <summary>
/// The keys the Platform itself declares.
/// <para>
/// Named here rather than as loose strings at each call site, so that the
/// declaration, the reader and the screen cannot drift apart — and so that
/// finding every changeable number in the Platform is one file rather than a
/// search.
/// </para>
/// <para>
/// All under the <c>platform.</c> namespace, which is the Platform's own and
/// which no registered application may declare into.
/// </para>
/// </summary>
public static class PlatformSettingKeys
{
    /// <summary>The application code the Platform's own settings belong to.</summary>
    public const string Namespace = "platform";

    /// <summary>How long a delivered outbox message is kept before it is swept.</summary>
    public const string OutboxProcessedRetention = "platform.outbox.processed-retention";

    /// <summary>How long a background job run is kept in the history.</summary>
    public const string JobHistoryRetention = "platform.jobs.history-retention";

    /// <summary>How long an outbound integration call log entry is kept.</summary>
    public const string IntegrationCallLogRetention = "platform.integrations.call-log-retention";

    /// <summary>
    /// How long a document marked for deletion waits before its content is
    /// destroyed.
    /// <para>
    /// The one on this list that somebody will genuinely want to change under
    /// pressure — a legal hold, or a company whose policy says ninety days — and
    /// the one where waiting for a deployment is worst, because the alternative
    /// is stopping the sweep by hand.
    /// </para>
    /// </summary>
    public const string DocumentDeletionGrace = "platform.documents.deletion-grace";
}
