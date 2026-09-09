using CCP.Modules.Integrations.Application.Abstractions;
using CCP.Modules.Integrations.Domain.Logging;
using CCP.Modules.Integrations.Domain.Outbound;
using CCP.Modules.Integrations.Domain.Providers;
using CCP.Modules.Integrations.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Api.IntegrationTests.Integrations;

/// <summary>
/// The governed door, exercised against a real HTTP server that misbehaves on
/// command.
/// <para>
/// <b>Phase 12's own test list asks for exactly this</b> — retry, circuit
/// breaker opening, timeout and logging, against a stub external service. Until
/// it existed, the resilience pipeline was verified by reading the code, which
/// is the kind of assurance that is worth exactly as much as the reader's
/// attention on the day.
/// </para>
/// <para>
/// <b>The outbound guard is replaced for these tests, and that is deliberate.</b>
/// The stub listens on <c>127.0.0.1</c>, which the guard refuses — correctly, and
/// with eighteen unit tests of its own saying so. Weakening the guard with a
/// production setting that permits loopback would be far worse than replacing it
/// here: a switch that disables an SSRF defence is a switch somebody eventually
/// turns on in the wrong environment. What is under test here is the pipeline
/// and the call log; what is under test there is the policy.
/// </para>
/// </summary>
public sealed class OutboundCallTests(PlatformApiFactory factory)
    : IClassFixture<PlatformApiFactory>, IAsyncLifetime, IDisposable
{
    private StubProvider _stub = null!;
    private LoopbackFactory _host = null!;

    public async Task InitializeAsync()
    {
        _stub = await StubProvider.StartAsync();
        _host = new LoopbackFactory(factory.TestConnectionString);
    }

    public async Task DisposeAsync()
    {
        await _stub.DisposeAsync();
    }

    /// <summary>
    /// The substitute host. Separate from the asynchronous teardown because it
    /// is the only thing here that disposes synchronously.
    /// </summary>
    public void Dispose()
    {
        _host?.Dispose();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AGoodCallSucceedsAndIsLogged()
    {
        string code = await RegisterProviderAsync(maxRetries: 0);

        IntegrationResponse response = await CallAsync(code);

        Assert.True(response.Succeeded);
        Assert.Equal(200, response.StatusCode);
        Assert.Equal(1, _stub.Requests);

        IntegrationCallLog log = await ReadLogAsync(response.CallLogId);

        Assert.Equal(CallOutcome.Succeeded, log.Outcome);
        Assert.Equal(1, log.Attempts);

        // The correlation id ties this call to the request that caused it, which
        // is the field an incident starts from.
        Assert.False(string.IsNullOrWhiteSpace(log.CorrelationId));
    }

    [Fact]
    public async Task ARetryActuallyRetries()
    {
        string code = await RegisterProviderAsync(maxRetries: 3);

        // Two failures, then success. A policy allowing three retries works
        // through them.
        _stub.FailuresRemaining = 2;

        IntegrationResponse response = await CallAsync(code);

        Assert.True(response.Succeeded);

        // The stub counted three arrivals. Nothing about the Platform's own log
        // is being trusted here — the count comes from the other end of the
        // socket.
        Assert.Equal(3, _stub.Requests);
        Assert.Equal(3, response.Attempts);
    }

    [Fact]
    public async Task AClientErrorIsNotRetried()
    {
        string code = await RegisterProviderAsync(maxRetries: 3);

        // A 400 means the request was wrong and will be wrong again. Retrying it
        // wastes time and adds load a provider did not ask for.
        _stub.FailuresRemaining = 5;
        _stub.FailureStatus = 400;

        IntegrationResponse response = await CallAsync(code);

        Assert.False(response.Succeeded);
        Assert.Equal(400, response.StatusCode);
        Assert.Equal(1, _stub.Requests);

        IntegrationCallLog log = await ReadLogAsync(response.CallLogId);

        // The provider answered, and said no. That is a completed call with a
        // bad status rather than a failure to reach them, and the two lead an
        // operator to different places.
        Assert.Equal(CallOutcome.Refused, log.Outcome);
    }

    [Fact]
    public async Task ATimeoutFires()
    {
        string code = await RegisterProviderAsync(maxRetries: 0, timeoutSeconds: 1);

        _stub.Delay = TimeSpan.FromSeconds(5);

        IntegrationResponse response = await CallAsync(code);

        Assert.Equal(CallOutcome.TimedOut, response.Outcome);

        IntegrationCallLog log = await ReadLogAsync(response.CallLogId);

        Assert.Equal(CallOutcome.TimedOut, log.Outcome);

        // Bounded by the provider's timeout rather than by the stub's delay,
        // which is the whole point of having one.
        Assert.True(log.DurationMs < 4_000, $"took {log.DurationMs}ms");
    }

    [Fact]
    public async Task TheCircuitOpensAndStopsCallingAFailingProvider()
    {
        string code = await RegisterProviderAsync(
            maxRetries: 0, failuresBeforeBreaking: 2, breakSeconds: 60);

        _stub.FailuresRemaining = int.MaxValue;

        // Enough failures to open it. The breaker measures a ratio over a
        // window, so it needs a handful of calls before it will act.
        for (int i = 0; i < 6; i++)
        {
            await CallAsync(code);
        }

        int requestsBefore = _stub.Requests;

        IntegrationResponse afterOpening = await CallAsync(code);

        Assert.Equal(CallOutcome.CircuitOpen, afterOpening.Outcome);

        // Nothing reached the provider. That is the point: the Platform stops
        // spending threads finding out what it already knows.
        Assert.Equal(requestsBefore, _stub.Requests);

        IntegrationCallLog log = await ReadLogAsync(afterOpening.CallLogId);

        // Logged rather than silent. "We did not call them" answers a question
        // that an absence of rows does not.
        Assert.Equal(CallOutcome.CircuitOpen, log.Outcome);
    }

    [Fact]
    public async Task ADisabledProviderIsNotCalledAndTheRefusalIsRecorded()
    {
        string code = await RegisterProviderAsync(maxRetries: 0);

        await SetEnabledAsync(code, isEnabled: false);

        IntegrationResponse response = await CallAsync(code);

        Assert.Equal(CallOutcome.Blocked, response.Outcome);
        Assert.Equal(0, _stub.Requests);
    }

    [Fact]
    public async Task SensitiveFieldsAreBlankedBeforeTheyAreStored()
    {
        string code = await RegisterProviderAsync(
            maxRetries: 0, redacted: ["cardNumber"]);

        _stub.SuccessBody = """{"receipt":"r_1","cardNumber":"4111111111111111"}""";

        IntegrationResponse response = await CallAsync(
            code, body: """{"amount":100,"cardNumber":"4111111111111111"}""");

        Assert.True(response.Succeeded);

        // The caller gets the real response — it asked for it.
        Assert.Contains("4111111111111111", response.Body!, StringComparison.Ordinal);

        IntegrationCallLog log = await ReadLogAsync(response.CallLogId);

        // The database does not, in either direction. Redaction happens before
        // storage, so there is no unredacted copy for the next export to find.
        Assert.DoesNotContain("4111111111111111", log.RequestPayload, StringComparison.Ordinal);
        Assert.DoesNotContain("4111111111111111", log.ResponsePayload, StringComparison.Ordinal);
        Assert.Contains("100", log.RequestPayload, StringComparison.Ordinal);
        Assert.Contains("r_1", log.ResponsePayload, StringComparison.Ordinal);
    }

    /// <summary>
    /// The guard is wired into the connector, not merely present.
    /// <para>
    /// The substitute used here allows loopback and refuses everything else, so
    /// a provider pointed anywhere but the stub is refused — which shows the
    /// connector consults the guard at all. That it consults <i>the right</i>
    /// guard in production is a registration, and that the real policy is
    /// correct is eighteen unit tests.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AHostTheGuardRefusesIsNotCalledAtAll()
    {
        string code = $"blocked{Guid.CreateVersion7():N}"[..20];

        await using (IntegrationDbContext db = Context())
        {
            IntegrationProvider provider = IntegrationProvider.Create(
                code, "Somewhere else", "https://not-allow-listed.invalid",
                DateTimeOffset.UtcNow).Value;

            provider.AddEndpoint("call", "POST", "v1/thing", DateTimeOffset.UtcNow);

            db.Providers.Add(provider);

            await db.SaveChangesAsync();
        }

        using IServiceScope scope = _host.Services.CreateScope();

        var connector = scope.ServiceProvider.GetRequiredService<IIntegrationConnector>();

        IntegrationResponse response = await connector.SendAsync(
            new IntegrationRequest(code, "call", Body: "{}"));

        // The loopback substitute still refuses this one, because it allows
        // loopback and nothing else.
        Assert.Equal(CallOutcome.Blocked, response.Outcome);
        Assert.Equal(0, _stub.Requests);
    }

    // -----------------------------------------------------------------------
    // Harness
    // -----------------------------------------------------------------------

    private IntegrationDbContext Context() =>
        new(new DbContextOptionsBuilder<IntegrationDbContext>()
            .UseNpgsql(factory.TestConnectionString)
            .Options);

    private async Task<string> RegisterProviderAsync(
        int maxRetries,
        int timeoutSeconds = 10,
        int failuresBeforeBreaking = 100,
        int breakSeconds = 30,
        IReadOnlyList<string>? redacted = null)
    {
        _stub.Reset();

        string code = $"stub{Guid.CreateVersion7():N}"[..20];

        await using IntegrationDbContext db = Context();

        DateTimeOffset now = DateTimeOffset.UtcNow;

        IntegrationProvider provider = IntegrationProvider.Create(
            code, "Stub provider", _stub.BaseAddress.ToString(), now).Value;

        provider.ConfigureResilience(
            TimeSpan.FromSeconds(timeoutSeconds),
            maxRetries,
            failuresBeforeBreaking,
            TimeSpan.FromSeconds(breakSeconds),
            maxConcurrentCalls: 8,
            now);

        if (redacted is not null)
        {
            provider.SetRedactionPolicy(redacted, now);
        }

        provider.AddEndpoint("call", "POST", "v1/thing", now);

        db.Providers.Add(provider);

        await db.SaveChangesAsync();

        return code;
    }

    private async Task SetEnabledAsync(string code, bool isEnabled)
    {
        await using IntegrationDbContext db = Context();

        IntegrationProvider provider = await db.Providers.FirstAsync(p => p.Code == code);

        provider.SetEnabled(isEnabled, DateTimeOffset.UtcNow);

        await db.SaveChangesAsync();
    }

    private async Task<IntegrationResponse> CallAsync(string code, string? body = null)
    {
        using IServiceScope scope = _host.Services.CreateScope();

        var connector = scope.ServiceProvider.GetRequiredService<IIntegrationConnector>();

        return await connector.SendAsync(
            new IntegrationRequest(code, "call", Body: body ?? """{"hello":"world"}"""));
    }

    private async Task<IntegrationCallLog> ReadLogAsync(Guid id)
    {
        await using IntegrationDbContext db = Context();

        return await db.CallLog.AsNoTracking().FirstAsync(e => e.Id == id);
    }

    /// <summary>
    /// The host with an outbound guard that permits loopback and nothing else.
    /// <para>
    /// One registration, replacing one interface. The connector, the pipeline,
    /// the redaction and the log are all the real ones — only the question "may
    /// the Platform reach this address" is answered differently, because the
    /// real answer for <c>127.0.0.1</c> is no and must stay no.
    /// </para>
    /// </summary>
    private sealed class LoopbackFactory(string connectionString) : PlatformApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            Environment.SetEnvironmentVariable(
                "CCP_ConnectionStrings__Platform", connectionString);

            base.ConfigureWebHost(builder);

            builder.ConfigureServices(services =>
                services.AddSingleton<IOutboundGuard, LoopbackOnlyGuard>());
        }
    }

    /// <summary>
    /// Allows the stub and refuses everything else.
    /// <para>
    /// Deliberately not "allows everything". A test double that permitted any
    /// address would let a genuinely unguarded call pass unnoticed, which is the
    /// opposite of what this suite is for.
    /// </para>
    /// </summary>
    private sealed class LoopbackOnlyGuard : IOutboundGuard
    {
        public Task<OutboundHostPolicy.Verdict> InspectAsync(
            Uri destination, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(destination);

            return Task.FromResult(
                destination.IsLoopback && destination.Scheme == Uri.UriSchemeHttp
                    ? OutboundHostPolicy.Verdict.Allowed
                    : OutboundHostPolicy.Verdict.HostNotAllowed);
        }
    }
}
