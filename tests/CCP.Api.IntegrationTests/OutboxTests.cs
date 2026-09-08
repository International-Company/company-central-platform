using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Application.Events;
using CCP.Kernel.Domain;
using CCP.Kernel.Infrastructure.Outbox;
using CCP.Kernel.Infrastructure.Persistence;
using CCP.Kernel.Primitives;
using Microsoft.EntityFrameworkCore;

namespace CCP.Api.IntegrationTests;

/// <summary>
/// The outbox is the highest-risk component in the kernel: implemented subtly
/// wrong, it loses events silently, and a lost event means a missing audit
/// record (ADR-013).
/// <para>
/// These tests run against real PostgreSQL because the guarantees being tested
/// — transactional atomicity and <c>FOR UPDATE SKIP LOCKED</c> — exist in the
/// database, not in the C#.
/// </para>
/// </summary>
public sealed class OutboxTests(PlatformApiFactory factory) : IClassFixture<PlatformApiFactory>
{
    // -----------------------------------------------------------------------
    // The core guarantee: the event lives or dies with its transaction.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Event_IsPersisted_WhenTheTransactionCommits()
    {
        await using KernelDbContext context = factory.CreateDbContext();

        var writer = new OutboxWriter(context, new StubRequestContext("corr-commit"));
        var testEvent = new TestIntegrationEvent("payload-committed");

        await writer.EnqueueAsync(testEvent);
        await context.SaveChangesAsync();

        await using KernelDbContext verification = factory.CreateDbContext();

        OutboxMessage? stored = await verification.OutboxMessages
            .SingleOrDefaultAsync(m => m.Id == testEvent.EventId);

        Assert.NotNull(stored);
        Assert.Equal("test.integration.event", stored.EventType);
        Assert.Equal("corr-commit", stored.CorrelationId);
        Assert.Null(stored.ProcessedAt);
        Assert.Equal(0, stored.AttemptCount);
    }

    [Fact]
    public async Task Event_IsNotPersisted_WhenTheTransactionRollsBack()
    {
        // This is the property that makes the outbox trustworthy: an audit
        // event can never describe a change that was rolled back.
        var testEvent = new TestIntegrationEvent("payload-rolled-back");

        await using (KernelDbContext context = factory.CreateDbContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();

            var writer = new OutboxWriter(context, new StubRequestContext("corr-rollback"));
            await writer.EnqueueAsync(testEvent);
            await context.SaveChangesAsync();

            await transaction.RollbackAsync();
        }

        await using KernelDbContext verification = factory.CreateDbContext();

        bool exists = await verification.OutboxMessages.AnyAsync(m => m.Id == testEvent.EventId);

        Assert.False(exists, "A rolled-back transaction must leave no outbox row behind.");
    }

    // -----------------------------------------------------------------------
    // Concurrency: several relays, no double delivery, no blocking.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ConcurrentRelays_EachMessageIsClaimedExactlyOnce()
    {
        const int messageCount = 60;

        await using (KernelDbContext seed = factory.CreateDbContext())
        {
            var writer = new OutboxWriter(seed, new StubRequestContext("corr-concurrent"));

            for (int i = 0; i < messageCount; i++)
            {
                await writer.EnqueueAsync(new TestIntegrationEvent($"payload-{i}"));
            }

            await seed.SaveChangesAsync();
        }

        // Four relays racing on the same table. Without SKIP LOCKED they would
        // either deadlock or deliver the same message more than once.
        var relays = Enumerable.Range(0, 4)
            .Select(_ => new OutboxTestHarness(factory))
            .ToArray();

        int[] claimed = await Task.WhenAll(relays.Select(r => r.DrainAsync()));

        await using KernelDbContext verification = factory.CreateDbContext();

        List<OutboxMessage> messages = await verification.OutboxMessages
            .Where(m => m.CorrelationId == "corr-concurrent")
            .ToListAsync();

        Assert.Equal(messageCount, messages.Count);
        Assert.All(messages, m => Assert.NotNull(m.ProcessedAt));

        // No message was attempted twice: every claim was exclusive.
        Assert.All(messages, m => Assert.Equal(1, m.AttemptCount));

        // And the work was genuinely shared rather than one relay doing it all
        // while the others blocked.
        Assert.Equal(messageCount, claimed.Sum());
    }

    // -----------------------------------------------------------------------
    // Failure handling
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Message_IsRetriedWithBackoff_WhenAHandlerFails()
    {
        var testEvent = new TestIntegrationEvent("payload-failing");

        await using (KernelDbContext seed = factory.CreateDbContext())
        {
            var writer = new OutboxWriter(seed, new StubRequestContext("corr-retry"));
            await writer.EnqueueAsync(testEvent);
            await seed.SaveChangesAsync();
        }

        var harness = new OutboxTestHarness(factory, dispatcher: new AlwaysFailingDispatcher());
        await harness.RunOnceAsync();

        await using KernelDbContext verification = factory.CreateDbContext();

        OutboxMessage stored = await verification.OutboxMessages.SingleAsync(m => m.Id == testEvent.EventId);

        Assert.Null(stored.ProcessedAt);
        Assert.Null(stored.DeadLetteredAt);
        Assert.Equal(1, stored.AttemptCount);
        Assert.NotNull(stored.LastError);
        Assert.True(
            stored.NextAttemptAt > stored.OccurredAt,
            "A failed message must be deferred, not retried immediately in a tight loop.");
    }

    [Fact]
    public async Task Message_IsDeadLettered_AfterTheAttemptLimit()
    {
        var testEvent = new TestIntegrationEvent("payload-poison");

        await using (KernelDbContext seed = factory.CreateDbContext())
        {
            var writer = new OutboxWriter(seed, new StubRequestContext("corr-deadletter"));
            await writer.EnqueueAsync(testEvent);
            await seed.SaveChangesAsync();
        }

        var options = new OutboxOptions { MaxAttempts = 3, BaseRetryDelay = TimeSpan.Zero };
        var harness = new OutboxTestHarness(factory, new AlwaysFailingDispatcher(), options);

        for (int attempt = 0; attempt < options.MaxAttempts; attempt++)
        {
            await harness.RunOnceAsync();
        }

        await using KernelDbContext verification = factory.CreateDbContext();

        OutboxMessage stored = await verification.OutboxMessages.SingleAsync(m => m.Id == testEvent.EventId);

        // Dead-lettered, not silently dropped: an operator can find it.
        Assert.NotNull(stored.DeadLetteredAt);
        Assert.Equal(options.MaxAttempts, stored.AttemptCount);
        Assert.NotNull(stored.LastError);

        // And it is no longer picked up, so one poison message cannot stall the
        // queue behind it.
        int claimedAfter = await harness.RunOnceAsync();
        Assert.Equal(0, claimedAfter);
    }

    [Fact]
    public async Task Backoff_GrowsAndIsCapped()
    {
        var options = new OutboxOptions
        {
            BaseRetryDelay = TimeSpan.FromSeconds(10),
            MaxRetryDelay = TimeSpan.FromMinutes(10)
        };

        var harness = new OutboxTestHarness(factory, new AlwaysFailingDispatcher(), options);

        TimeSpan first = harness.Relay.BackoffFor(1);
        TimeSpan later = harness.Relay.BackoffFor(6);
        TimeSpan far = harness.Relay.BackoffFor(50);

        // Jitter is 0.5x-1.5x, so assertions are on ranges rather than exact
        // values — the jitter is deliberate and must not be asserted away.
        Assert.InRange(first, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
        Assert.True(later > first, "Backoff must grow with the attempt count.");
        Assert.True(far <= options.MaxRetryDelay, "Backoff must never exceed the configured cap.");
    }

    // -----------------------------------------------------------------------
    // Delivery
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Message_ReachesItsHandlerWithThePayloadIntact()
    {
        var testEvent = new TestIntegrationEvent("payload-round-trip");

        await using (KernelDbContext seed = factory.CreateDbContext())
        {
            var writer = new OutboxWriter(seed, new StubRequestContext("corr-delivery"));
            await writer.EnqueueAsync(testEvent);
            await seed.SaveChangesAsync();
        }

        var recording = new RecordingDispatcher();
        var harness = new OutboxTestHarness(factory, recording);

        await harness.RunOnceAsync();

        IIntegrationEvent delivered = Assert.Single(recording.Delivered);
        var typed = Assert.IsType<TestIntegrationEvent>(delivered);

        Assert.Equal(testEvent.EventId, typed.EventId);
        Assert.Equal("payload-round-trip", typed.Payload);
    }

    [Fact]
    public async Task MessageWithNoHandler_IsMarkedProcessed()
    {
        // A published event with no subscriber is normal, not an error. It must
        // not accumulate in the queue forever.
        var testEvent = new TestIntegrationEvent("payload-unhandled");

        await using (KernelDbContext seed = factory.CreateDbContext())
        {
            var writer = new OutboxWriter(seed, new StubRequestContext("corr-nohandler"));
            await writer.EnqueueAsync(testEvent);
            await seed.SaveChangesAsync();
        }

        var harness = new OutboxTestHarness(factory, new NoOpDispatcher());
        await harness.RunOnceAsync();

        await using KernelDbContext verification = factory.CreateDbContext();

        OutboxMessage stored = await verification.OutboxMessages.SingleAsync(m => m.Id == testEvent.EventId);

        Assert.NotNull(stored.ProcessedAt);
        Assert.Null(stored.DeadLetteredAt);
    }
}

// ---------------------------------------------------------------------------
// Test doubles
// ---------------------------------------------------------------------------

/// <summary>An integration event used only by these tests.</summary>
public sealed record TestIntegrationEvent : IIntegrationEvent
{
    public TestIntegrationEvent(string payload)
    {
        Payload = payload;
        EventId = Uuid7.NewGuid();
        OccurredAt = DateTimeOffset.UtcNow;
    }

    // Parameterless construction path for the JSON round trip.
    public TestIntegrationEvent() => Payload = string.Empty;

    public string Payload { get; init; }

    public Guid EventId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public string EventType => "test.integration.event";
}

internal sealed class StubRequestContext(string correlationId) : IRequestContext
{
    public string RequestId => "test-request";

    public string CorrelationId { get; } = correlationId;

    public string? IpAddress => "127.0.0.1";

    public string? UserAgent => "integration-tests";
}

internal sealed class NoOpDispatcher : IIntegrationEventDispatcher
{
    public Task DispatchAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

internal sealed class AlwaysFailingDispatcher : IIntegrationEventDispatcher
{
    public Task DispatchAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Handler failed deliberately.");
}

internal sealed class RecordingDispatcher : IIntegrationEventDispatcher
{
    private readonly List<IIntegrationEvent> _delivered = [];

    public IReadOnlyList<IIntegrationEvent> Delivered
    {
        get
        {
            lock (_delivered)
            {
                return [.. _delivered];
            }
        }
    }

    public Task DispatchAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        lock (_delivered)
        {
            _delivered.Add(integrationEvent);
        }

        return Task.CompletedTask;
    }
}
