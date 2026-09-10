using CCP.Kernel.Application.Configuration;
using CCP.Kernel.Application.Jobs;
using CCP.Kernel.Infrastructure.Outbox;
using CCP.Kernel.Infrastructure.Persistence;
using CCP.Kernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CCP.Api.IntegrationTests;

/// <summary>
/// The sweep that removes delivered outbox rows.
/// <para>
/// <b>It is the only thing in the kernel that deletes rows on a timer</b>, and
/// the one property that must never break is what it refuses to touch: a
/// dead-lettered message is an event that will never be delivered, which means
/// an audit entry or a notification is permanently missing and somebody has to
/// decide what to do about it. A sweep that quietly took those away would erase
/// the evidence of the one failure mode the whole outbox exists to make visible,
/// and nothing anywhere would report it.
/// </para>
/// <para>
/// It also settles a question that cannot be answered by reading the code:
/// whether <c>ExecuteDelete</c> with a bound <c>Take</c> is translatable at all
/// on Npgsql. If it is not, the sweep throws once every six hours, the job
/// journal records a failure nobody is watching yet, and the table grows for
/// ever while the code looks correct.
/// </para>
/// </summary>
public sealed class OutboxRetentionTests(PlatformApiFactory factory)
    : IClassFixture<PlatformApiFactory>
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 10, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ADeliveredMessagePastItsRetentionIsRemoved()
    {
        Guid id = await AMessageAsync(processedAt: Now.AddDays(-30));

        await SweepAsync();

        Assert.False(await ExistsAsync(id));
    }

    /// <summary>
    /// A message delivered this morning stays. The retention window exists so
    /// that "did that event go out on Tuesday" is still answerable.
    /// </summary>
    [Fact]
    public async Task ARecentlyDeliveredMessageIsKept()
    {
        Guid id = await AMessageAsync(processedAt: Now.AddHours(-2));

        await SweepAsync();

        Assert.True(await ExistsAsync(id));
    }

    /// <summary>
    /// An undelivered message stays, however old.
    /// <para>
    /// An old unprocessed row means the relay has been stuck for a month, which
    /// is a thing to be alarmed about rather than tidied away. Deleting it would
    /// drop an event that was never delivered at all.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AnUndeliveredMessageIsNeverRemoved()
    {
        Guid id = await AMessageAsync(processedAt: null);

        await SweepAsync();

        Assert.True(await ExistsAsync(id));
    }

    /// <summary>
    /// <b>The assertion this class exists for.</b> A dead-lettered message is
    /// kept for ever, no matter how long ago it was given up on.
    /// </summary>
    [Fact]
    public async Task ADeadLetteredMessageIsNeverRemoved()
    {
        Guid id = await AMessageAsync(
            processedAt: null,
            deadLetteredAt: Now.AddYears(-2));

        await SweepAsync();

        Assert.True(await ExistsAsync(id));
    }

    /// <summary>
    /// A row that was both delivered and dead-lettered — which the relay should
    /// never produce, and which the sweep must still not remove.
    /// <para>
    /// The filter reads <c>ProcessedAt != null &amp;&amp; DeadLetteredAt ==
    /// null</c>. Written as two independent conditions it would be one edit away
    /// from dropping the dead-letter half, and this is the case that would
    /// notice.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AMessageThatIsBothDeliveredAndDeadLetteredIsKept()
    {
        Guid id = await AMessageAsync(
            processedAt: Now.AddDays(-30),
            deadLetteredAt: Now.AddDays(-30));

        await SweepAsync();

        Assert.True(await ExistsAsync(id));
    }

    /// <summary>
    /// The pass reports what it removed, and says nothing when there was
    /// nothing to remove.
    /// </summary>
    [Fact]
    public async Task ThePassReportsWhatItDidAndIsSilentOtherwise()
    {
        Assert.Null(await SweepAsync());

        await AMessageAsync(processedAt: Now.AddDays(-30));

        string? summary = await SweepAsync();

        Assert.NotNull(summary);
        Assert.Contains("Removed", summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// More rows than one batch holds are all removed, across several batches
    /// within the pass.
    /// <para>
    /// The batching is what keeps the first-ever sweep from being one enormous
    /// transaction on the busiest table in the database. A loop that stopped
    /// after the first batch would look identical in every other test here.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ABacklogLargerThanOneBatchIsFullyRemoved()
    {
        var ids = new List<Guid>();

        for (int i = 0; i < 7; i++)
        {
            ids.Add(await AMessageAsync(processedAt: Now.AddDays(-30)));
        }

        // A batch size of two, so seven rows need four passes of the inner loop.
        await SweepAsync(batchSize: 2);

        foreach (Guid id in ids)
        {
            Assert.False(await ExistsAsync(id), $"{id} survived the sweep.");
        }
    }

    // --- Fixtures -----------------------------------------------------------

    private async Task<string?> SweepAsync(int batchSize = 1000)
    {
        var sweep = new OutboxRetentionSweep(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new OutboxOptions
            {
                ProcessedRetention = TimeSpan.FromDays(7),
                RetentionBatchSize = batchSize,
                RetentionMaxRowsPerPass = 50_000
            }),

            // The real settings reader, resolved from the host. The Platform
            // declares this retention on startup with the value it ships with,
            // which is the same seven days passed above -- so the sweep behaves
            // identically whether the setting is read or the fallback is used.
            // That equality is the seeder's contract, and this is one place it
            // would show if it ever stopped holding.
            factory.Services.GetRequiredService<IPlatformSettings>(),
            new FixedClock(Now),
            factory.Services.GetRequiredService<JobRunner>());

        return await sweep.SweepAsync(CancellationToken.None);
    }

    private async Task<Guid> AMessageAsync(
        DateTimeOffset? processedAt,
        DateTimeOffset? deadLetteredAt = null)
    {
        await using KernelDbContext context = factory.CreateDbContext();

        var message = new OutboxMessage
        {
            Id = Uuid7.NewGuid(),
            EventType = "retention.probe",
            PayloadType = typeof(OutboxRetentionTests).FullName!,
            Payload = "{}",
            OccurredAt = Now.AddDays(-31),
            ProcessedAt = processedAt,
            DeadLetteredAt = deadLetteredAt,
            NextAttemptAt = Now.AddDays(-31)
        };

        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();

        return message.Id;
    }

    private async Task<bool> ExistsAsync(Guid id)
    {
        await using KernelDbContext context = factory.CreateDbContext();

        return await context.OutboxMessages.AnyAsync(message => message.Id == id);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
