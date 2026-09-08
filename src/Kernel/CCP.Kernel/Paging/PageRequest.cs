using CCP.Kernel.Results;

namespace CCP.Kernel.Paging;

/// <summary>
/// A validated request for one page of a collection (ADR-008).
/// <para>
/// Constructed through <see cref="Create"/> so an invalid page size cannot
/// exist: the cap is enforced at construction rather than trusted to each
/// endpoint. An uncapped page size is a denial-of-service vector.
/// </para>
/// </summary>
public sealed record PageRequest
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    private PageRequest(int page, int pageSize, string? sort)
    {
        Page = page;
        PageSize = pageSize;
        Sort = sort;
    }

    /// <summary>1-based page number.</summary>
    public int Page { get; }

    public int PageSize { get; }

    /// <summary>Raw sort expression, e.g. <c>-createdAt</c>. Validated per endpoint.</summary>
    public string? Sort { get; }

    /// <summary>Rows to skip. Convenience for query composition.</summary>
    public int Skip => (Page - 1) * PageSize;

    public static Result<PageRequest> Create(int? page, int? pageSize, string? sort = null)
    {
        int resolvedPage = page ?? 1;
        int resolvedPageSize = pageSize ?? DefaultPageSize;

        if (resolvedPage < 1)
        {
            return Error.Validation(
                "PLATFORM.INVALID_PAGE",
                "Page must be 1 or greater.",
                "page");
        }

        if (resolvedPageSize < 1 || resolvedPageSize > MaxPageSize)
        {
            return Error.Validation(
                "PLATFORM.INVALID_PAGE_SIZE",
                $"Page size must be between 1 and {MaxPageSize}.",
                "pageSize");
        }

        return new PageRequest(resolvedPage, resolvedPageSize, sort);
    }
}
