using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Audit.Application.Abstractions;
using CCP.Modules.Audit.Contracts.Dtos;
using CCP.Modules.Audit.Domain;

namespace CCP.Modules.Audit.Application;

/// <summary>Audit module configuration.</summary>
public sealed class AuditOptions
{
    public const string SectionName = "Audit";

    /// <summary>
    /// The widest window a single search may cover.
    /// <para>
    /// Ninety days. Wide enough for the investigations people actually run,
    /// narrow enough that one query cannot walk years of partitions. Anything
    /// larger is an export, which is asynchronous and does not hold a request
    /// open while it works.
    /// </para>
    /// </summary>
    public int MaxSearchWindowDays { get; set; } = 90;

    /// <summary>
    /// The most events one ingestion request may carry.
    /// <para>
    /// Batching exists for volume, but an unbounded batch is a way to make one
    /// request consume the memory of the whole process.
    /// </para>
    /// </summary>
    public int MaxBatchSize { get; set; } = 500;

    /// <summary>
    /// How many months of partitions to keep ready ahead of now.
    /// <para>
    /// Three. A partitioned table rejects a row that has no partition, and the
    /// first minute of a new month is the worst possible time to find that out.
    /// </para>
    /// </summary>
    public int PartitionsAheadMonths { get; set; } = 3;
}

/// <summary>Searching the trail.</summary>
public sealed record SearchAuditQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Application,
    string? Module,
    string? Action,
    Guid? ActorUserId,
    string? ResourceType,
    string? ResourceId,
    string? Result,
    int Skip,
    int Take);

/// <summary>
/// Reads the trail, with the date bounds enforced rather than suggested.
/// </summary>
public sealed class SearchAuditHandler(
    IAuditRepository repository,
    Microsoft.Extensions.Options.IOptions<AuditOptions> options)
{
    private readonly AuditOptions _options = options.Value;

    public async Task<Result<(IReadOnlyList<AuditEventDto> Items, long Total)>> HandleAsync(
        SearchAuditQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Required, not defaulted. A default window would let a caller who
        // omitted the dates believe they had searched everything, which in an
        // investigation is worse than an error.
        if (query.From is not { } from || query.To is not { } to)
        {
            return Result.Failure<(IReadOnlyList<AuditEventDto>, long)>(AuditErrors.DateRangeRequired);
        }

        if (from > to)
        {
            return Result.Failure<(IReadOnlyList<AuditEventDto>, long)>(AuditErrors.DateRangeInverted);
        }

        if ((to - from).TotalDays > _options.MaxSearchWindowDays)
        {
            return Result.Failure<(IReadOnlyList<AuditEventDto>, long)>(AuditErrors.DateRangeTooWide);
        }

        AuditResult? result = null;

        if (!string.IsNullOrWhiteSpace(query.Result))
        {
            if (!Enum.TryParse(query.Result, ignoreCase: true, out AuditResult parsed))
            {
                return Result.Failure<(IReadOnlyList<AuditEventDto>, long)>(Error.Validation(
                    "AUDIT.INVALID_RESULT",
                    $"Unknown result. Allowed: {string.Join(", ", Enum.GetNames<AuditResult>())}.",
                    "result"));
            }

            result = parsed;
        }

        var criteria = new AuditSearchCriteria(
            from, to,
            Normalize(query.Application),
            Normalize(query.Module),
            Normalize(query.Action),
            query.ActorUserId,
            query.ResourceType,
            query.ResourceId,
            result);

        (IReadOnlyList<AuditEvent> items, long total) =
            await repository.SearchAsync(criteria, query.Skip, query.Take, cancellationToken);

        return Result.Success<(IReadOnlyList<AuditEventDto>, long)>(([.. items.Select(Map)], total));
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    internal static AuditEventDto Map(AuditEvent e)
        => new(
            e.Id, e.OccurredAt, e.Application, e.Module, e.Action, e.Result.ToString(),
            e.ResourceType, e.ResourceId, e.ActorUserId, e.ActorUsername, e.OnBehalfOfUserId,
            e.IpAddress, e.CorrelationId, e.OldValue, e.NewValue, e.Metadata);
}

/// <summary>An external application submitting events attributed to itself.</summary>
public sealed record IngestAuditCommand(
    string Application,
    IReadOnlyList<IngestAuditEventDto> Events,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId);

/// <summary>
/// Accepts events from a business application.
/// <para>
/// <b>The application is taken from the caller's identity, never from the
/// body.</b> A request that could name its own application would let any holder
/// of an ingestion credential write entries attributed to Finance or HR, and an
/// audit trail anyone can forge is not evidence of anything.
/// </para>
/// </summary>
public sealed class IngestAuditHandler(
    IAuditRepository repository,
    IClock clock,
    Microsoft.Extensions.Options.IOptions<AuditOptions> options)
{
    private readonly AuditOptions _options = options.Value;

    public async Task<Result<IngestionResultDto>> HandleAsync(
        IngestAuditCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Events.Count == 0)
        {
            return Result.Failure<IngestionResultDto>(AuditErrors.BatchEmpty);
        }

        if (command.Events.Count > _options.MaxBatchSize)
        {
            return Result.Failure<IngestionResultDto>(AuditErrors.BatchTooLarge);
        }

        DateTimeOffset now = clock.UtcNow;
        var events = new List<AuditEvent>(command.Events.Count);
        List<Error> errors = [];

        for (int i = 0; i < command.Events.Count; i++)
        {
            IngestAuditEventDto dto = command.Events[i];

            if (string.IsNullOrWhiteSpace(dto.Module))
            {
                errors.Add(AuditErrors.ModuleRequired);
                continue;
            }

            if (string.IsNullOrWhiteSpace(dto.Action))
            {
                errors.Add(AuditErrors.ActionRequired);
                continue;
            }

            DateTimeOffset occurredAt = dto.OccurredAt ?? now;

            // A small allowance for clock skew between machines, then a refusal.
            // An event dated next week is a broken clock or a forgery, and
            // silently rewriting the timestamp would erase the evidence of
            // either.
            if (occurredAt > now.AddMinutes(5))
            {
                errors.Add(AuditErrors.OccurredInTheFuture);
                continue;
            }

            AuditResult result = AuditResult.Success;

            if (!string.IsNullOrWhiteSpace(dto.Result)
                && !Enum.TryParse(dto.Result, ignoreCase: true, out result))
            {
                errors.Add(Error.Validation(
                    "AUDIT.INVALID_RESULT",
                    $"Unknown result. Allowed: {string.Join(", ", Enum.GetNames<AuditResult>())}.",
                    $"events[{i}].result"));

                continue;
            }

            events.Add(AuditEvent.Record(
                command.Application,
                dto.Module,
                dto.Action,
                occurredAt,
                result,
                dto.ResourceType,
                dto.ResourceId,
                dto.ActorUserId,
                dto.ActorUsername,
                dto.OnBehalfOfUserId,
                command.IpAddress,
                command.UserAgent,
                correlationId: command.CorrelationId,
                oldValue: dto.OldValue,
                newValue: dto.NewValue,
                metadata: dto.Metadata));
        }

        if (errors.Count > 0)
        {
            // All-or-nothing. A partially accepted batch leaves the caller
            // unable to say what was recorded, and retrying would duplicate
            // whatever succeeded.
            return Result.Failure<IngestionResultDto>([.. errors]);
        }

        int written = await repository.AppendAsync(events, cancellationToken);

        return Result.Success(new IngestionResultDto(written));
    }
}
