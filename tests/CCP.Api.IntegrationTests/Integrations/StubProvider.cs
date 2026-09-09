using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace CCP.Api.IntegrationTests.Integrations;

/// <summary>
/// An external service that misbehaves on command.
/// <para>
/// <b>A real HTTP server on a real socket</b>, because the thing being tested is
/// whether a retry actually retries, a breaker actually opens and a timeout
/// actually fires — and a substitute that returns a canned
/// <c>HttpResponseMessage</c> proves none of that. It proves the substitute
/// works.
/// </para>
/// <para>
/// It counts the requests it receives, which is how a test can tell one attempt
/// from four without reading the Platform's own log and thereby testing the log
/// against itself.
/// </para>
/// </summary>
public sealed class StubProvider : IAsyncDisposable
{
    private readonly WebApplication _app;
    private int _requests;

    private StubProvider(WebApplication app, Uri address)
    {
        _app = app;
        BaseAddress = address;
    }

    /// <summary>Where the Platform should be pointed.</summary>
    public Uri BaseAddress { get; }

    /// <summary>How many requests actually arrived.</summary>
    public int Requests => Volatile.Read(ref _requests);

    /// <summary>
    /// How many of the next requests fail before the stub starts succeeding.
    /// <para>
    /// Set to a number a retry policy can work through, and the call succeeds
    /// after that many attempts. Set higher than the policy allows, and it does
    /// not.
    /// </para>
    /// </summary>
    public int FailuresRemaining { get; set; }

    /// <summary>The status returned while failing.</summary>
    public int FailureStatus { get; set; } = StatusCodes.Status503ServiceUnavailable;

    /// <summary>How long every response takes. For proving a timeout fires.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    /// <summary>The body returned on success.</summary>
    public string SuccessBody { get; set; } = """{"status":"ok"}""";

    /// <summary>
    /// Starts on a port the operating system chooses.
    /// <para>
    /// Port zero rather than a fixed number, so several tests can run at once
    /// and a developer with something already on 5000 is not the reason the
    /// suite fails.
    /// </para>
    /// </summary>
    public static async Task<StubProvider> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();

        builder.Logging.ClearProviders();
        // Port zero: the operating system picks one. A fixed number would make
        // two tests running at once fight over it.
        builder.WebHost.ConfigureKestrel(
            kestrel => kestrel.ListenLocalhost(0));

        WebApplication app = builder.Build();

        StubProvider? provider = null;

        app.MapPost("/{**path}", async (HttpContext context) =>
        {
            StubProvider stub = provider!;

            Interlocked.Increment(ref stub._requests);

            if (stub.Delay > TimeSpan.Zero)
            {
                await Task.Delay(stub.Delay, context.RequestAborted);
            }

            if (stub.FailuresRemaining > 0)
            {
                stub.FailuresRemaining--;

                return Results.StatusCode(stub.FailureStatus);
            }

            return Results.Content(stub.SuccessBody, "application/json");
        });

        await app.StartAsync();

        string address = app.Services
            .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features
            .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!
            .Addresses
            .First();

        provider = new StubProvider(app, new Uri(address, UriKind.Absolute));

        return provider;
    }

    /// <summary>Forgets what it has seen, so one test can measure two calls.</summary>
    public void Reset()
    {
        Volatile.Write(ref _requests, 0);
        FailuresRemaining = 0;
        Delay = TimeSpan.Zero;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"stub at {BaseAddress}");
}
