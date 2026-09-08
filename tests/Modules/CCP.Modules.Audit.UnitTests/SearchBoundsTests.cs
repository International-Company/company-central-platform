using CCP.Kernel.Results;
using CCP.Modules.Audit.Application;
using CCP.Modules.Audit.Application.Abstractions;
using CCP.Modules.Audit.Contracts.Dtos;
using CCP.Modules.Audit.Domain;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Audit.UnitTests;

/// <summary>
/// The date bounds on search.
/// <para>
/// An unbounded query over a table designed to grow forever is an outage, and it
/// is caused by someone opening a dashboard. These rules are the difference
/// between a search feature and an incident.
/// </para>
/// </summary>
public sealed class SearchBoundsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private static SearchAuditHandler CreateHandler(RecordingRepository? repository = null)
        => new(repository ?? new RecordingRepository(), Options.Create(new AuditOptions()));

    private static SearchAuditQuery Query(DateTimeOffset? from, DateTimeOffset? to)
        => new(from, to, null, null, null, null, null, null, null, 0, 25);

    [Fact]
    public async Task Search_RefusesAMissingStart()
    {
        Result<(IReadOnlyList<AuditEventDto>, long)> result =
            await CreateHandler().HandleAsync(Query(null, Now));

        // Not defaulted to some window. A caller who omitted the dates would
        // otherwise believe they had searched everything — which, in an
        // investigation, is worse than an error.
        Assert.True(result.IsFailure);
        Assert.Equal(AuditErrors.DateRangeRequired.Code, result.Errors[0].Code);
    }

    [Fact]
    public async Task Search_RefusesAMissingEnd()
    {
        Result<(IReadOnlyList<AuditEventDto>, long)> result =
            await CreateHandler().HandleAsync(Query(Now.AddDays(-1), null));

        Assert.True(result.IsFailure);
        Assert.Equal(AuditErrors.DateRangeRequired.Code, result.Errors[0].Code);
    }

    [Fact]
    public async Task Search_RefusesAnInvertedRange()
    {
        Result<(IReadOnlyList<AuditEventDto>, long)> result =
            await CreateHandler().HandleAsync(Query(Now, Now.AddDays(-1)));

        Assert.True(result.IsFailure);
        Assert.Equal(AuditErrors.DateRangeInverted.Code, result.Errors[0].Code);
    }

    [Fact]
    public async Task Search_RefusesAWindowWiderThanThePolicyAllows()
    {
        Result<(IReadOnlyList<AuditEventDto>, long)> result =
            await CreateHandler().HandleAsync(Query(Now.AddDays(-200), Now));

        // One query walking years of partitions is indistinguishable from an
        // outage while it runs. Wider ranges are an export, which is
        // asynchronous.
        Assert.True(result.IsFailure);
        Assert.Equal(AuditErrors.DateRangeTooWide.Code, result.Errors[0].Code);
    }

    [Fact]
    public async Task Search_AcceptsAWindowInsideThePolicy()
    {
        var repository = new RecordingRepository();

        Result<(IReadOnlyList<AuditEventDto>, long)> result =
            await CreateHandler(repository).HandleAsync(Query(Now.AddDays(-30), Now));

        Assert.True(result.IsSuccess);
        Assert.NotNull(repository.LastCriteria);
    }

    [Fact]
    public async Task Search_RefusesAnUnknownResultFilter()
    {
        Result<(IReadOnlyList<AuditEventDto>, long)> result = await CreateHandler().HandleAsync(
            new SearchAuditQuery(
                Now.AddDays(-1), Now, null, null, null, null, null, null, "catastrophic", 0, 25));

        Assert.True(result.IsFailure);
        Assert.Equal("AUDIT.INVALID_RESULT", result.Errors[0].Code);
    }

    [Fact]
    public async Task Search_NormalisesTheClassifierFilters()
    {
        var repository = new RecordingRepository();

        await CreateHandler(repository).HandleAsync(new SearchAuditQuery(
            Now.AddDays(-1), Now, "  PLATFORM ", "Identity", "User.Created",
            null, null, null, null, 0, 25));

        // Stored lower-cased, so a filter typed in any case has to be lowered
        // too or it silently matches nothing.
        Assert.Equal("platform", repository.LastCriteria!.Application);
        Assert.Equal("identity", repository.LastCriteria.Module);
        Assert.Equal("user.created", repository.LastCriteria.Action);
    }

    private sealed class RecordingRepository : IAuditRepository
    {
        public AuditSearchCriteria? LastCriteria { get; private set; }

        public Task<int> AppendAsync(
            IReadOnlyList<AuditEvent> events, CancellationToken cancellationToken = default)
            => Task.FromResult(events.Count);

        public Task<(IReadOnlyList<AuditEvent> Items, long TotalCount)> SearchAsync(
            AuditSearchCriteria criteria, int skip, int take, CancellationToken cancellationToken = default)
        {
            LastCriteria = criteria;

            return Task.FromResult<(IReadOnlyList<AuditEvent>, long)>(([], 0));
        }

        public Task EnsurePartitionsAsync(
            DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
