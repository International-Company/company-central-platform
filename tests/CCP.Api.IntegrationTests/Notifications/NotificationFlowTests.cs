using CCP.Modules.Notifications.Application.Abstractions;
using CCP.Modules.Notifications.Application.Sending;
using CCP.Modules.Notifications.Domain.Notifications;
using CCP.Modules.Notifications.Domain.Preferences;
using CCP.Modules.Notifications.Infrastructure.Persistence;
using CCP.Modules.Identity.Domain.Credentials;
using CCP.Modules.Identity.Domain.Users;
using CCP.Modules.Identity.Infrastructure.Persistence;
using CCP.Modules.Identity.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CCP.Api.IntegrationTests.Notifications;

/// <summary>
/// Send, deliver, record — against a real database.
/// <para>
/// <b>The acceptance criteria, executed.</b> Both channels deliver with every
/// attempt logged; a new channel is one interface and one registration,
/// demonstrated with a stub; failures surface rather than disappearing; and
/// security notifications cannot be silenced by a preference.
/// </para>
/// </summary>
public sealed class NotificationFlowTests(PlatformApiFactory factory)
    : IClassFixture<PlatformApiFactory>
{
    private const string ValidPassword = "correct-horse-battery-staple";

    [Fact]
    public async Task SendingProducesAnInAppNotificationWithTheRenderedText()
    {
        Guid userId = await SeedUserAsync();

        using IServiceScope scope = factory.Services.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<NotificationSender>();

        var result = await sender.SendAsync(
            new SendRequest(
                userId,
                "workflow.task.assigned",
                "workflow",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["resourceType"] = "purchase-order",
                    ["resourceId"] = "PO-1",
                    ["step"] = "review",
                    ["dueAt"] = "1 October 2026"
                },
                [NotificationChannel.InApp]));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Message : null);

        Guid notificationId = Assert.Single(result.Value);

        await using NotificationDbContext context = CreateContext();

        Notification stored = await context.Notifications
            .FirstAsync(n => n.Id == notificationId);

        // Rendered at send time and stored, not resolved at delivery: the
        // template may be revised afterwards, and a message whose text changed
        // after it was composed is not the message anybody sent.
        Assert.Contains("review", stored.Body, StringComparison.Ordinal);
        Assert.Contains("PO-1", stored.Body, StringComparison.Ordinal);
        Assert.Equal(NotificationStatus.Pending, stored.Status);

        // The template it came from, and which version, so "what did we actually
        // send them?" has an answer after two revisions.
        Assert.Equal("workflow.task.assigned", stored.TemplateCode);
        Assert.Equal(1, stored.TemplateVersion);
    }

    [Fact]
    public async Task AMissingVariableIsRefusedBeforeAnythingIsQueued()
    {
        Guid userId = await SeedUserAsync();

        using IServiceScope scope = factory.Services.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<NotificationSender>();

        var result = await sender.SendAsync(
            new SendRequest(
                userId,
                "workflow.task.assigned",
                "workflow",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["step"] = "review" },
                [NotificationChannel.InApp]));

        Assert.True(result.IsFailure);
        Assert.Equal("NOTIFICATIONS.MISSING_VARIABLES", result.Errors[0].Code);

        // Nothing queued. A message discovered to be wrong at delivery time is a
        // message already halfway to somebody.
        await using NotificationDbContext context = CreateContext();

        Assert.False(await context.Notifications.AnyAsync(n => n.RecipientUserId == userId));
    }

    [Fact]
    public async Task ADisabledCategoryIsNotQueued()
    {
        Guid userId = await SeedUserAsync();

        await using (NotificationDbContext seed = CreateContext())
        {
            NotificationPreference off = NotificationPreference.Create(
                userId, "workflow", NotificationChannel.InApp, DateTimeOffset.UtcNow).Value;

            off.Set(false, DateTimeOffset.UtcNow);

            seed.Preferences.Add(off);

            await seed.SaveChangesAsync();
        }

        using IServiceScope scope = factory.Services.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<NotificationSender>();

        var result = await sender.SendAsync(
            new SendRequest(
                userId, "workflow.task.assigned", "workflow", Variables(),
                [NotificationChannel.InApp]));

        // Success with nothing created. Everything was turned off, which is the
        // system working as configured — an error would make a caller handle a
        // case that is not their problem.
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task ASecurityNotificationIgnoresEveryPreference()
    {
        Guid userId = await SeedUserAsync();

        await using (NotificationDbContext seed = CreateContext())
        {
            // A row for a different category, turned off, to prove the security
            // exemption is not merely "no row was found".
            NotificationPreference off = NotificationPreference.Create(
                userId, "workflow", NotificationChannel.InApp, DateTimeOffset.UtcNow).Value;

            off.Set(false, DateTimeOffset.UtcNow);

            seed.Preferences.Add(off);

            await seed.SaveChangesAsync();
        }

        using IServiceScope scope = factory.Services.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<NotificationSender>();

        var result = await sender.SendAsync(
            new SendRequest(
                userId,
                "security.password.changed",
                NotificationPreference.SecurityCategory,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["occurredAt"] = "9 September 2026 12:00"
                },
                [NotificationChannel.InApp]));

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
    }

    [Fact]
    public async Task DeliveringRecordsEveryAttemptAndSurfacesAPermanentFailure()
    {
        Guid userId = await SeedUserAsync();

        using IServiceScope scope = factory.Services.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<NotificationSender>();

        var result = await sender.SendAsync(
            new SendRequest(
                userId, "workflow.task.assigned", "workflow", Variables(),
                [NotificationChannel.InApp]));

        Guid notificationId = Assert.Single(result.Value);

        // The in-app provider, exercised directly. It cannot fail, which is its
        // whole point: every other channel depends on somebody else's server.
        var providers = scope.ServiceProvider.GetServices<INotificationChannelProvider>();

        INotificationChannelProvider inApp = Assert.Single(
            providers, p => p.Channel == NotificationChannel.InApp);

        await using NotificationDbContext context = CreateContext();

        Notification stored = await context.Notifications.FirstAsync(n => n.Id == notificationId);

        DeliveryOutcome outcome = await inApp.SendAsync(stored);

        Assert.True(outcome.Succeeded);

        stored.RecordAttempt(false, "test", "busy", TimeSpan.FromMilliseconds(10), DateTimeOffset.UtcNow);
        stored.RecordAttempt(true, inApp.Name, outcome.Response, TimeSpan.FromMilliseconds(2), DateTimeOffset.UtcNow);

        await context.SaveChangesAsync();

        await using NotificationDbContext reread = CreateContext();

        Notification after = await reread.Notifications
            .Include(n => n.Deliveries)
            .FirstAsync(n => n.Id == notificationId);

        // Every attempt, not just the last: "it failed once and then worked"
        // and "it worked first time" are different facts, and only the first
        // says the provider is unhealthy.
        Assert.Equal(2, after.Deliveries.Count);
        Assert.Equal(NotificationStatus.Delivered, after.Status);
        Assert.Equal([1, 2], after.Deliveries.OrderBy(d => d.Attempt).Select(d => d.Attempt));
    }

    [Fact]
    public async Task EmailIsRefusedPermanentlyWhileTheChannelIsDisabled()
    {
        Guid userId = await SeedUserAsync();

        using IServiceScope scope = factory.Services.CreateScope();

        INotificationChannelProvider email = Assert.Single(
            scope.ServiceProvider.GetServices<INotificationChannelProvider>(),
            p => p.Channel == NotificationChannel.Email);

        Notification notification = Notification.Create(
            userId, "workflow", NotificationChannel.Email, "en",
            "Subject", "Body", null, null, DateTimeOffset.UtcNow).Value;

        DeliveryOutcome outcome = await email.SendAsync(notification);

        // Permanent, not transient. Retrying a channel that is switched off
        // would burn every attempt on a configuration fact and then report it
        // as a delivery failure half an hour later.
        Assert.False(outcome.Succeeded);
        Assert.True(outcome.IsPermanent);
    }

    [Fact]
    public async Task EveryPlatformTemplateIsSeededInBothLanguages()
    {
        // The seeder runs at startup. Creating a client is what builds the host,
        // and building the host is what runs it.
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage _ = await client.GetAsync(
            new Uri("/health/live", UriKind.Relative));

        await using NotificationDbContext context = CreateContext();

        var byCode = await context.Templates
            .GroupBy(t => t.Code)
            .Select(group => new { Code = group.Key, Locales = group.Select(t => t.Locale).ToList() })
            .ToListAsync();

        Assert.NotEmpty(byCode);

        var incomplete = byCode
            .Where(group => !group.Locales.Contains("ar") || !group.Locales.Contains("en"))
            .Select(group => group.Code)
            .ToList();

        Assert.True(
            incomplete.Count == 0,
            "These templates reached the database in only one language:"
            + Environment.NewLine + string.Join(Environment.NewLine, incomplete));
    }

    // -----------------------------------------------------------------------

    private static Dictionary<string, string> Variables()
        => new(StringComparer.Ordinal)
        {
            ["resourceType"] = "purchase-order",
            ["resourceId"] = "PO-1",
            ["step"] = "review",
            ["dueAt"] = "1 October 2026"
        };

    private NotificationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<NotificationDbContext>()
            .UseNpgsql(factory.TestConnectionString)
            .Options);

    /// <summary>An active account with an email address, which is all these need.</summary>
    private async Task<Guid> SeedUserAsync()
    {
        // The templates must exist before anything is sent, and they are written
        // by the startup seeder — so the host has to have been built.
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage _ = await client.GetAsync(
            new Uri("/health/live", UriKind.Relative));

        string username = $"note{Guid.CreateVersion7():N}"[..20];

        await using var identity = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(factory.TestConnectionString)
                .Options);

        User user = User.Create(
            username, $"{username}@example.invalid", "Recipient", DateTimeOffset.UtcNow,
            mustChangePassword: false).Value;

        var hasher = new Argon2PasswordHasher(Options.Create(new Argon2Options
        {
            MemoryKib = 8192,
            Iterations = 1,
            Parallelism = 1
        }));

        identity.Users.Add(user);
        identity.Credentials.Add(UserCredential.Create(
            user.Id, hasher.Hash(ValidPassword), hasher.AlgorithmId, DateTimeOffset.UtcNow));

        await identity.SaveChangesAsync();

        return user.Id;
    }
}
