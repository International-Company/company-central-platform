using CCP.Kernel.Results;

namespace CCP.Modules.Audit.Domain;

/// <summary>Every failure the Audit module can produce.</summary>
public static class AuditErrors
{
    public static readonly Error DateRangeRequired = Error.Validation(
        "AUDIT.DATE_RANGE_REQUIRED",
        "A date range is required. An unbounded audit query scans a table that grows forever.",
        "from");

    public static readonly Error DateRangeInverted = Error.Validation(
        "AUDIT.DATE_RANGE_INVERTED", "The start of the range must not be after its end.", "from");

    /// <summary>
    /// The window is capped. A single query that walks years of partitions is
    /// indistinguishable from an outage while it runs.
    /// </summary>
    public static readonly Error DateRangeTooWide = Error.Validation(
        "AUDIT.DATE_RANGE_TOO_WIDE",
        "The date range is too wide. Narrow it, or export instead.",
        "to");

    public static readonly Error ApplicationRequired = Error.Validation(
        "AUDIT.APPLICATION_REQUIRED", "An application is required.", "application");

    public static readonly Error ActionRequired = Error.Validation(
        "AUDIT.ACTION_REQUIRED", "An action is required.", "action");

    public static readonly Error ModuleRequired = Error.Validation(
        "AUDIT.MODULE_REQUIRED", "A module is required.", "module");

    /// <summary>
    /// A caller may only write events attributed to itself (ARCHITECTURE.md
    /// §15.3). Otherwise any application holding an ingestion credential could
    /// forge the trail of every other one, which would make the whole record
    /// worthless as evidence.
    /// </summary>
    public static readonly Error ApplicationMismatch = Error.Forbidden(
        "AUDIT.APPLICATION_MISMATCH",
        "Events may only be written for the application making the request.");

    public static readonly Error BatchTooLarge = Error.Validation(
        "AUDIT.BATCH_TOO_LARGE", "Too many events in one request.", "events");

    public static readonly Error BatchEmpty = Error.Validation(
        "AUDIT.BATCH_EMPTY", "The batch contains no events.", "events");

    /// <summary>
    /// Refused rather than silently corrected. An event dated next year is a
    /// broken clock or a forgery attempt, and quietly rewriting its timestamp
    /// would destroy the evidence of either.
    /// </summary>
    public static readonly Error OccurredInTheFuture = Error.Validation(
        "AUDIT.OCCURRED_IN_THE_FUTURE", "An event cannot have occurred in the future.", "occurredAt");
}
