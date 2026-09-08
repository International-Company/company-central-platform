using CCP.Kernel.Results;

namespace CCP.Kernel.Paging;

/// <summary>A parsed, allow-listed sort instruction.</summary>
/// <param name="Field">The field to sort by, as named by the API.</param>
/// <param name="Descending">True when the client requested <c>-field</c>.</param>
public sealed record SortSpec(string Field, bool Descending)
{
    /// <summary>
    /// Parses a sort expression against an allow-list of sortable fields.
    /// <para>
    /// The allow-list is mandatory. Sorting by an arbitrary client-supplied
    /// column invites a full table scan on an unindexed column, which is an
    /// easy denial of service on a large table (ADR-008).
    /// </para>
    /// </summary>
    /// <param name="sort">The raw expression, e.g. <c>-createdAt</c>. May be null.</param>
    /// <param name="allowedFields">Field names that are indexed and safe to sort by.</param>
    public static Result<SortSpec?> Parse(string? sort, IReadOnlySet<string> allowedFields)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return Result.Success<SortSpec?>(null);
        }

        string trimmed = sort.Trim();
        bool descending = trimmed.StartsWith('-');
        string field = descending ? trimmed[1..] : trimmed;

        if (field.Length == 0)
        {
            return Error.Validation("PLATFORM.INVALID_SORT", "Sort field must not be empty.", "sort");
        }

        if (!allowedFields.Contains(field))
        {
            return Error.Validation(
                "PLATFORM.SORT_FIELD_NOT_ALLOWED",
                $"Sorting by '{field}' is not supported. Allowed: {string.Join(", ", allowedFields.Order())}.",
                "sort");
        }

        return Result.Success<SortSpec?>(new SortSpec(field, descending));
    }
}
