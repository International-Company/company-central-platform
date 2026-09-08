using CCP.Kernel.Application.Events;
using CCP.Kernel.Infrastructure.Outbox;
using CCP.Kernel.Infrastructure.Persistence;
using CCP.Kernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CCP.Api.IntegrationTests;

/// <summary>
/// Drives <see cref="OutboxRelay"/> one pass at a time.
/// <para>
/// The relay is a <c>BackgroundService</c> that normally loops on a timer.
/// Tests need to control exactly when a pass happens, so this harness builds a
/// relay with its own service scope and calls the batch method directly —
/// deterministic, and no sleeping.
/// </para>
/// </summary>
internal sealed class OutboxTestHarness
{
    private readonly ServiceProvider _serviceProvider;

    public OutboxTestHarness(
        PlatformApiFactory factory,
        IIntegrationEventDispatcher? dispatcher = null,
        OutboxOptions? options = null)
    {
        var services = new ServiceCollection();

        services.AddDbContext<KernelDbContext>(builder =>
            builder.UseNpgsql(factory.TestConnectionString));

        services.AddSingleton(dispatcher ?? new NoOpDispatcher());

        _serviceProvider = services.BuildServiceProvider();

        Relay = new OutboxRelay(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options ?? new OutboxOptions()),
            new SystemClock(),
            NullLogger<OutboxRelay>.Instance);
    }

    public OutboxRelay Relay { get; }

    /// <summary>Runs a single relay pass. Returns the number of messages claimed.</summary>
    public Task<int> RunOnceAsync() => Relay.ProcessBatchAsync(CancellationToken.None);

    /// <summary>
    /// Runs passes until nothing is left to claim. Used by the concurrency test,
    /// where several harnesses drain the same queue simultaneously.
    /// </summary>
    public async Task<int> DrainAsync()
    {
        int total = 0;
        int claimed;

        do
        {
            claimed = await RunOnceAsync();
            total += claimed;
        }
        while (claimed > 0);

        return total;
    }
}
