using CCP.Modules.Notifications.Application.Abstractions;
using CCP.Modules.Notifications.Domain.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Api.IntegrationTests.Notifications;

/// <summary>
/// A third channel, added the way the documentation says.
/// <para>
/// <b>Phase 9's acceptance criterion asks for this to be demonstrated, not
/// asserted.</b> So it is: one class implementing one interface, one
/// registration line, and nothing else changed — no edit to the dispatcher, to
/// the templates, to the sender, or to any caller.
/// </para>
/// <para>
/// The value of writing it as a test rather than as a paragraph is that it
/// stops being true the moment somebody adds a switch on channel type
/// somewhere. A paragraph would not notice.
/// </para>
/// </summary>
public sealed class ChannelExtensibilityTests(PlatformApiFactory factory)
    : IClassFixture<PlatformApiFactory>
{
    /// <summary>
    /// A channel the Platform has never heard of, delivering nowhere.
    /// <para>
    /// It uses the <c>Sms</c> slot because the enum has one — designed for and
    /// deliberately not built (§17.2). A real SMS provider would differ from
    /// this only in the body of <c>SendAsync</c>.
    /// </para>
    /// </summary>
    private sealed class StubChannelProvider : INotificationChannelProvider
    {
        public NotificationChannel Channel => NotificationChannel.Sms;

        public string Name => "stub";

        /// <summary>What it was asked to deliver, for the assertions below.</summary>
        public List<Notification> Sent { get; } = [];

        public Task<DeliveryOutcome> SendAsync(
            Notification notification,
            CancellationToken cancellationToken = default)
        {
            Sent.Add(notification);

            return Task.FromResult(DeliveryOutcome.Delivered("stub accepted"));
        }
    }

    [Fact]
    public void AChannelIsAddedByRegisteringOneImplementation()
    {
        var stub = new StubChannelProvider();

        using var withStub = new StubbedFactory(stub, factory.TestConnectionString);

        using IServiceScope scope = withStub.Services.CreateScope();

        IReadOnlyList<INotificationChannelProvider> providers =
            [.. scope.ServiceProvider.GetServices<INotificationChannelProvider>()];

        // The dispatcher resolves whatever is registered and matches by channel.
        // It never learns which channels exist, which is what makes the third
        // one a registration rather than a change.
        Assert.Contains(providers, p => p.Channel == NotificationChannel.InApp);
        Assert.Contains(providers, p => p.Channel == NotificationChannel.Email);
        Assert.Contains(providers, p => p.Channel == NotificationChannel.Sms);
    }

    [Fact]
    public async Task TheNewChannelDeliversThroughTheSameContract()
    {
        var stub = new StubChannelProvider();

        using var withStub = new StubbedFactory(stub, factory.TestConnectionString);

        using IServiceScope scope = withStub.Services.CreateScope();

        INotificationChannelProvider resolved = Assert.Single(
            scope.ServiceProvider.GetServices<INotificationChannelProvider>(),
            p => p.Channel == NotificationChannel.Sms);

        Notification notification = Notification.Create(
            Guid.CreateVersion7(), "workflow", NotificationChannel.Sms, "en",
            "Subject", "Body", null, null, DateTimeOffset.UtcNow).Value;

        DeliveryOutcome outcome = await resolved.SendAsync(notification);

        Assert.True(outcome.Succeeded);
        Assert.Equal("stub accepted", outcome.Response);
        Assert.Single(stub.Sent);
    }

    /// <summary>
    /// The host with one extra provider registered, and nothing else changed.
    /// <para>
    /// A singleton instance so the test can read what it was asked to send —
    /// the only thing here that a production registration would do differently.
    /// </para>
    /// <para>
    /// <b>It takes the connection string rather than finding one.</b> This
    /// factory is constructed by hand, so xUnit never calls its
    /// <c>InitializeAsync</c> and it never publishes the environment variable
    /// the composition root reads before the host is built. It passed for
    /// several phases purely because some other fixture had already set that
    /// variable first — an ordering the suite never guaranteed, and which
    /// adding a module's tests was enough to change. Borrowing the class
    /// fixture's database makes it independent of what else is running.
    /// </para>
    /// </summary>
    private sealed class StubbedFactory(StubChannelProvider stub, string connectionString)
        : PlatformApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            // Before base, because the composition root reads this off
            // configuration before the host exists — anything contributed later
            // arrives after the read that would have thrown.
            Environment.SetEnvironmentVariable(
                "CCP_ConnectionStrings__Platform", connectionString);

            base.ConfigureWebHost(builder);

            builder.ConfigureServices(services =>
                services.AddSingleton<INotificationChannelProvider>(stub));
        }
    }
}
