using CCP.Kernel.Paging;
using CCP.Kernel.Results;

namespace CCP.Kernel.UnitTests.Paging;

/// <summary>
/// Paging is validated centrally so no endpoint has to remember the rules
/// (ADR-008). These tests cover the two that matter for safety: the page size
/// cap, and the sort allow-list.
/// </summary>
public sealed class PageRequestTests
{
    [Fact]
    public void Create_AppliesDefaults_WhenNothingSupplied()
    {
        Result<PageRequest> result = PageRequest.Create(null, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Page);
        Assert.Equal(PageRequest.DefaultPageSize, result.Value.PageSize);
        Assert.Equal(0, result.Value.Skip);
    }

    [Theory]
    [InlineData(1, 25, 0)]
    [InlineData(2, 25, 25)]
    [InlineData(5, 10, 40)]
    public void Skip_IsComputedFromPageAndSize(int page, int pageSize, int expectedSkip)
    {
        Result<PageRequest> result = PageRequest.Create(page, pageSize);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedSkip, result.Value.Skip);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_RejectsNonPositivePage(int page)
    {
        Result<PageRequest> result = PageRequest.Create(page, 25);

        Assert.True(result.IsFailure);
        Assert.Equal("PLATFORM.INVALID_PAGE", result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]
    [InlineData(10_000)]
    public void Create_RejectsPageSizeOutsideTheAllowedRange(int pageSize)
    {
        // The cap is the point: an uncapped page size lets one request ask for
        // the entire table, which is a denial of service on a large one.
        Result<PageRequest> result = PageRequest.Create(1, pageSize);

        Assert.True(result.IsFailure);
        Assert.Equal("PLATFORM.INVALID_PAGE_SIZE", result.Error.Code);
    }

    [Fact]
    public void Create_AcceptsTheMaximumPageSize()
    {
        Result<PageRequest> result = PageRequest.Create(1, PageRequest.MaxPageSize);

        Assert.True(result.IsSuccess);
        Assert.Equal(PageRequest.MaxPageSize, result.Value.PageSize);
    }
}

public sealed class SortSpecTests
{
    private static readonly IReadOnlySet<string> Allowed =
        new HashSet<string>(StringComparer.Ordinal) { "name", "createdAt" };

    [Fact]
    public void Parse_ReturnsNull_WhenNoSortRequested()
    {
        Result<SortSpec?> result = SortSpec.Parse(null, Allowed);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Parse_ReadsAscendingField()
    {
        Result<SortSpec?> result = SortSpec.Parse("name", Allowed);

        Assert.True(result.IsSuccess);
        Assert.Equal("name", result.Value!.Field);
        Assert.False(result.Value.Descending);
    }

    [Fact]
    public void Parse_ReadsDescendingField()
    {
        Result<SortSpec?> result = SortSpec.Parse("-createdAt", Allowed);

        Assert.True(result.IsSuccess);
        Assert.Equal("createdAt", result.Value!.Field);
        Assert.True(result.Value.Descending);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("passwordHash")]
    [InlineData("-secretColumn")]
    [InlineData("name; DROP TABLE users")]
    public void Parse_RejectsFieldsOutsideTheAllowList(string sort)
    {
        // The allow-list is a security and performance control, not a
        // convenience: it prevents both sorting on an unindexed column and any
        // attempt to smuggle SQL through the parameter.
        Result<SortSpec?> result = SortSpec.Parse(sort, Allowed);

        Assert.True(result.IsFailure);
        Assert.Equal("PLATFORM.SORT_FIELD_NOT_ALLOWED", result.Error.Code);
    }

    [Fact]
    public void Parse_RejectsBareMinusSign()
    {
        Result<SortSpec?> result = SortSpec.Parse("-", Allowed);

        Assert.True(result.IsFailure);
        Assert.Equal("PLATFORM.INVALID_SORT", result.Error.Code);
    }

    [Fact]
    public void Parse_IsCaseSensitive()
    {
        // Field names map to API contract names, which are camelCase. Accepting
        // any casing would make the contract ambiguous.
        Result<SortSpec?> result = SortSpec.Parse("Name", Allowed);

        Assert.True(result.IsFailure);
    }
}

public sealed class PagedResultTests
{
    [Theory]
    [InlineData(137, 25, 6)]
    [InlineData(100, 25, 4)]
    [InlineData(1, 25, 1)]
    [InlineData(0, 25, 0)]
    public void TotalPages_RoundsUp(long totalItems, int pageSize, int expected)
    {
        var result = new PagedResult<string>([], 1, pageSize, totalItems);

        Assert.Equal(expected, result.TotalPages);
    }

    [Fact]
    public void HasPreviousAndNext_ReflectPosition()
    {
        var first = new PagedResult<string>([], page: 1, pageSize: 25, totalItems: 137);
        var middle = new PagedResult<string>([], page: 3, pageSize: 25, totalItems: 137);
        var last = new PagedResult<string>([], page: 6, pageSize: 25, totalItems: 137);

        Assert.False(first.HasPrevious);
        Assert.True(first.HasNext);

        Assert.True(middle.HasPrevious);
        Assert.True(middle.HasNext);

        Assert.True(last.HasPrevious);
        Assert.False(last.HasNext);
    }

    [Fact]
    public void TotalPages_IsZero_WhenPageSizeIsZero()
    {
        // Guards against a division by zero if a caller ever constructs this
        // directly rather than through PageRequest.
        var result = new PagedResult<string>([], 1, 0, 100);

        Assert.Equal(0, result.TotalPages);
    }
}
