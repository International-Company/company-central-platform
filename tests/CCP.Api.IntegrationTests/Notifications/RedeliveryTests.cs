using CCP.Modules.Notifications.Application.Sending;
using CCP.Modules.Notifications.Domain.Notifications;
using CCP.Modules.Notifications.Domain.Templates;
using CCP.Modules.Notifications.Infrastructure.Persistence;
using CCP.Kernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Api.IntegrationTests.Notifications;

/// <summary>
/// The same event delivered twice produces one notification.
/// <para>
/// <b>Outbox delivery is at-least-once by design.</b> A crash between
/// dispatching a message and marking it processed causes a redelivery, and that
/// is the right trade: the alternative loses events, and a lost event means a
/// missing audit record or a notification nobody ever gets. The price is that a
/// listener can be handed the same event twice.
/// </para>
/// <para>
/// <b>Only a database can answer this.</b> The marker and the notification are
/// written in one <c>SaveChanges</c>, and the claim being tested is that the
/// second delivery finds the first one's row — which is a statement about a
/// transaction, not about C#.
/// </para>
/// </summary>
public sealed class RedeliveryTests(PlatformApiFactory factory)
    : IClassFixture<PlatformApiFactory>
{
    private const string TemplateCode = "redelivery.probe";

    [Fact]
    public async Task TheSameEventTwiceProducesOneNotification()
    {
        await ATemplateAsync();

        Guid recipient = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();

        IReadOnlyList<Guid> first = await SendAsync(recipient, eventId);
        IReadOnlyList<Guid> second = await SendAsync(recipient, eventId);

        Assert.NotEmpty(first);

        // Empty, and a *success*. The caller asked for somebody to be told and
        // somebody has been told; reporting an error would make every listener
        // handle a case that is not a problem.
        Assert.Empty(second);

        await using NotificationDbContext context = Notifications();

        Assert.Equal(
            first.Count,
            await context.Notifications.CountAsync(n => n.RecipientUserId == recipient));
    }

    /// <summary>
    /// A different event to the same person still gets through.
    /// <para>
    /// Without this the suite would pass on a build that deduplicated on the
    /// recipient, or on the template, or on nothing at all after the first send
    /// — every one of which silently stops people being told things.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ADifferentEventIsNotMistakenForARedelivery()
    {
        await ATemplateAsync();

        Guid recipient = Guid.CreateVersion7();

        Assert.NotEmpty(await SendAsync(recipient, Guid.CreateVersion7()));
        Assert.NotEmpty(await SendAsync(recipient, Guid.CreateVersion7()));

        await using NotificationDbContext context = Notifications();

        Assert.True(
            await context.Notifications.CountAsync(n => n.RecipientUserId == recipient) >= 2,
            "The second, unrelated event was swallowed.");
    }

    /// <summary>
    /// A send with no event behind it is never deduplicated.
    /// <para>
    /// Those are the sends a handler makes directly. They are not redelivered
    /// and have nothing to deduplicate against, so treating two of them as one
    /// would drop a message somebody deliberately asked to send twice.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ASendWithNoEventBehindItIsAlwaysDelivered()
    {
        await ATemplateAsync();

        Guid recipient = Guid.CreateVersion7();

        Assert.NotEmpty(await SendAsync(recipient, causedBy: null));
        Assert.NotEmpty(await SendAsync(recipient, causedBy: null));
    }

    /// <summary>
    /// The marker really is in the same transaction, which is what makes this a
    /// guarantee rather than a reassurance.
    /// </summary>
    [Fact]
    public async Task TheMarkerIsWrittenWithTheNotification()
    {
        await ATemplateAsync();

        Guid eventId = Guid.CreateVersion7();

        await SendAsync(Guid.CreateVersion7(), eventId);

        await using NotificationDbContext context = Notifications();

        Assert.True(
            await context.ProcessedEvents
                .AnyAsync(e => e.EventId == eventId && e.Reason == TemplateCode),
            "The notification was written and the marker was not, so a redelivery would duplicate it.");
    }

    // --- Fixtures -----------------------------------------------------------

    private NotificationDbContext Notifications() =>
        new(new DbContextOptionsBuilder<NotificationDbContext>()
            .UseNpgsql(factory.TestConnectionString)
            .Options);

    private async Task<IReadOnlyList<Guid>> SendAsync(Guid recipient, Guid? causedBy)
    {
        using IServiceScope scope = factory.Services.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<NotificationSender>();

        Result<IReadOnlyList<Guid>> result = await sender.SendAsync(
            new SendRequest(
                recipient,
                TemplateCode,
                "workflow",
                new Dictionary<string, string>(StringComparer.Ordinal),
                [NotificationChannel.InApp],
                causedBy));

        Assert.True(result.IsSuccess, $"The send failed: {result.Errors[0].Code}");

        return result.Value;
    }

    /// <summary>
    /// A template with no variables, in both languages, so the send does not
    /// depend on which language the recipient is written to in.
    /// </summary>
    private async Task ATemplateAsync()
    {
        await using NotificationDbContext context = Notifications();

        if (await context.Templates.AnyAsync(t => t.Code == TemplateCode))
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (string locale in new[] { "ar", "en" })
        {
            context.Templates.Add(NotificationTemplate.Create(
                TemplateCode,
                locale,
                "Redelivery probe",
                "A message with nothing in it.",
                [],
                now).Value);
        }

        await context.SaveChangesAsync();
    }
}
