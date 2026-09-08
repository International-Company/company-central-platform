using CCP.Modules.Audit.Domain;

namespace CCP.Modules.Audit.Application.Abstractions;

/// <summary>
/// Writes and reads the trail.
/// <para>
/// <b>There is no update and no delete.</b> Not because they were forgotten, but
/// because the interface is one of the two places immutability is enforced: the
/// other is the database role, which holds <c>INSERT</c> and <c>SELECT</c> and
/// nothing more (ARCHITECTURE.md §15.4). Code discipline without the privilege
/// is a promise; the privilege without the discipline is an accident waiting to
/// happen.
/// </para>
/// </summary>
public interface IAuditRepository
{
    /// <summary>
    /// Appends events and commits them immediately.
    /// <para>
    /// Its own transaction, on its own context. An audit write must not fail the
    /// operation it is recording, and must not be rolled back with it: a denied
    /// action rolls back everything it touched, and the record of that denial is
    /// precisely what must survive.
    /// </para>
    /// </summary>
    Task<int> AppendAsync(
        IReadOnlyList<AuditEvent> events, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches the trail. The date range is required, not optional.
    /// </summary>
    Task<(IReadOnlyList<AuditEvent> Items, long TotalCount)> SearchAsync(
        AuditSearchCriteria criteria,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the monthly partitions covering a window exist.
    /// <para>
    /// Creating them ahead of time rather than on demand: a partitioned table
    /// with no partition for the current row rejects the insert, and the first
    /// minute of a new month is a poor time to discover that.
    /// </para>
    /// </summary>
    Task EnsurePartitionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>
/// What a search asks for.
/// <para>
/// The filters here are exactly the ones the composite indexes serve
/// (ARCHITECTURE.md §15.5). Adding a filter without adding an index turns a
/// bounded query into a scan of a table designed to grow forever.
/// </para>
/// </summary>
public sealed record AuditSearchCriteria(
    DateTimeOffset From,
    DateTimeOffset To,
    string? Application = null,
    string? Module = null,
    string? Action = null,
    Guid? ActorUserId = null,
    string? ResourceType = null,
    string? ResourceId = null,
    AuditResult? Result = null);

/// <summary>
/// The seam every module uses to record an audit event.
/// <para>
/// Deliberately narrow, and deliberately not the repository. A module should be
/// able to say "this happened" without knowing anything about partitions,
/// transactions or the audit schema — and without being able to reach anything
/// it should not.
/// </para>
/// </summary>
public interface IAuditRecorder
{
    Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task RecordAsync(
        IReadOnlyList<AuditEvent> auditEvents, CancellationToken cancellationToken = default);
}
