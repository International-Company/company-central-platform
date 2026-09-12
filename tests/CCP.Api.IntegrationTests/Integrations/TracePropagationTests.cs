using System.Diagnostics;
using System.Net.Http.Json;
using CCP.Modules.Integrations.Application.Abstractions;
using CCP.Modules.Integrations.Domain.Providers;
using CCP.Modules.Integrations.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Api.IntegrationTests.Integrations;

/// <summary>
/// A trace that arrives at the Platform leaves it again.
/// <para>
/// <b>Debt #56 named the gap precisely: the round trip was untested.</b> The
/// Platform accepts an inbound correlation id and echoes it, and outbound calls
/// go through an instrumented client — both true, and neither of them says that
/// a business system's trace actually joins the Platform's and comes back out
/// the other side.
/// </para>
/// <para>
/// That question can only be answered from outside. Asserting it from within the
/// Platform would mean the code that might be wrong is the code doing the
/// asserting — so the stub provider records what it was sent, and the test reads
/// the headers that really crossed the wire.
/// </para>
/// <para>
/// It matters because a trace is the only thing that turns "the invoice system
/// says the approval failed" and "the Platform says nothing went wrong" into one
/// timeline. Two systems with unrelated trace ids produce two stories and no way
/// to lay them side by side.
/// </para>
/// </summary>
public sealed class TracePropagationTests(PlatformApiFactory factory)
    : IClassFixture<PlatformApiFactory>, IAsyncLifetime, IDisposable
{
    private StubProvider _stub = null!;
    private LoopbackTraceFactory _host = null!;

    public async Task InitializeAsync()
    {
        _stub = await StubProvider.StartAsync();
        _host = new LoopbackTraceFactory(factory.TestConnectionString);
    }

    public async Task DisposeAsync() => await _stub.DisposeAsync();

    public void Dispose()
    {
        _host?.Dispose();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The trace the Platform was called on is the trace it calls out on.
    /// <para>
    /// <b>The whole of #56 in one assertion.</b> A <c>traceparent</c> is
    /// accepted, an outbound call is made while it is current, and the trace id
    /// on the wire going out is the one that came in.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ATraceThatArrivesIsTheTraceThatLeaves()
    {
        // A listener is what makes Activity.StartActivity return anything at
        // all. Without one the runtime declines to create activities, and a test
        // written without it passes by never having a trace to lose.
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded
        };

        ActivitySource.AddActivityListener(listener);

        using var source = new ActivitySource("ccp.tests.trace");
        using Activity? inbound = source.StartActivity("business-system-call");

        Assert.NotNull(inbound);

        string code = await RegisterProviderAsync();

        IntegrationResponse response = await CallAsync(code);

        Assert.True(response.Succeeded);

        Assert.True(
            _stub.LastHeaders.TryGetValue("traceparent", out string? traceparent),
            "The outbound call carried no traceparent. A business system's trace "
            + "cannot join the Platform's if the Platform does not pass it on.");

        // The W3C form is version-traceid-spanid-flags, and it is the trace id
        // that has to survive. The span id must not: this is a different span,
        // a child of the one that arrived, and reusing the id would collapse two
        // operations into one in every viewer.
        string[] parts = traceparent!.Split('-');

        Assert.Equal(4, parts.Length);
        Assert.Equal(inbound.TraceId.ToHexString(), parts[1]);
        Assert.NotEqual(inbound.SpanId.ToHexString(), parts[2]);
    }

    /// <summary>
    /// And the correlation id the Platform answers with is the one it was given.
    /// <para>
    /// The other half of the round trip, and the half a person uses. A trace id
    /// is for a tracing backend; a correlation id is what somebody quotes in a
    /// support ticket, and it has to be the same string the caller sent or the
    /// two halves of an incident cannot be joined by hand.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheCorrelationIdComesBackUnchanged()
    {
        using HttpClient client = factory.CreateClient();

        const string correlationId = "trace-propagation-suite-0001";

        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/diagnostics/ping", UriKind.Relative));

        Assert.True(response.Headers.TryGetValues("X-Correlation-Id", out IEnumerable<string>? echoed));
        Assert.Equal(correlationId, echoed!.Single());
    }

    // --- Fixtures -----------------------------------------------------------

    private IntegrationDbContext Context() =>
        new(new DbContextOptionsBuilder<IntegrationDbContext>()
            .UseNpgsql(factory.TestConnectionString)
            .Options);

    private async Task<string> RegisterProviderAsync()
    {
        _stub.Reset();

        // Version 4, not 7: a truncated UUIDv7 is mostly millisecond timestamp
        // and collides between tests running in the same instant.
        string code = $"trace{Guid.NewGuid():N}"[..20];

        await using IntegrationDbContext db = Context();

        DateTimeOffset now = DateTimeOffset.UtcNow;

        IntegrationProvider provider = IntegrationProvider.Create(
            code, "Trace stub", _stub.BaseAddress.ToString(), now).Value;

        provider.ConfigureResilience(
            TimeSpan.FromSeconds(10), 0, 100, TimeSpan.FromSeconds(30), 8, now);

        provider.AddEndpoint("call", "POST", "v1/thing", now);

        db.Providers.Add(provider);

        await db.SaveChangesAsync();

        return code;
    }

    private async Task<IntegrationResponse> CallAsync(string code)
    {
        using IServiceScope scope = _host.Services.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<IIntegrationConnector>()
            .SendAsync(new IntegrationRequest(code, "call", Body: "{}"));
    }

    /// <summary>
    /// The host with a guard that permits loopback, so the stub can be reached.
    /// The real policy refuses it, correctly, and is tested where it belongs.
    /// </summary>
    private sealed class LoopbackTraceFactory(string connectionString) : PlatformApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            Environment.SetEnvironmentVariable(
                "CCP_ConnectionStrings__Platform", connectionString);

            base.ConfigureWebHost(builder);

            builder.ConfigureServices(services =>
                services.AddSingleton<IOutboundGuard, LoopbackTraceGuard>());
        }
    }

    private sealed class LoopbackTraceGuard : IOutboundGuard
    {
        public Task<Modules.Integrations.Domain.Outbound.OutboundHostPolicy.Verdict> InspectAsync(
            Uri destination, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(destination);

            return Task.FromResult(
                destination.IsLoopback
                    ? Modules.Integrations.Domain.Outbound.OutboundHostPolicy.Verdict.Allowed
                    : Modules.Integrations.Domain.Outbound.OutboundHostPolicy.Verdict.HostNotAllowed);
        }

        public Task<Modules.Integrations.Domain.Outbound.OutboundRoute> ApproveAsync(
            string host, CancellationToken cancellationToken = default)
            => Task.FromResult(
                System.Net.IPAddress.TryParse(host, out System.Net.IPAddress? address)
                && System.Net.IPAddress.IsLoopback(address)
                    ? Modules.Integrations.Domain.Outbound.OutboundRoute.Allowed([address])
                    : Modules.Integrations.Domain.Outbound.OutboundRoute.Refused(
                        Modules.Integrations.Domain.Outbound.OutboundHostPolicy.Verdict.HostNotAllowed));
    }
}
