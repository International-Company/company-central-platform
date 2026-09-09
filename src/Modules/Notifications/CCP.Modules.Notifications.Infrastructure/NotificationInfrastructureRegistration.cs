using CCP.Modules.Notifications.Application.Abstractions;
using CCP.Modules.Notifications.Infrastructure.Channels;
using CCP.Modules.Notifications.Infrastructure.Dispatch;
using CCP.Modules.Notifications.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Modules.Notifications.Infrastructure;

/// <summary>
/// Registers the Notifications module's infrastructure.
/// <para>
/// The provider registrations are the interesting lines: adding a channel is
/// one more <c>AddScoped&lt;INotificationChannelProvider, …&gt;</c> and nothing
/// else. The dispatcher resolves whatever is registered and matches by channel,
/// so it never learns which channels exist.
/// </para>
/// </summary>
public static class NotificationInfrastructureRegistration
{
    public static IServiceCollection AddNotificationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<NotificationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable(
                    "__ef_migrations_history", NotificationDbContext.SchemaName)));

        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationUnitOfWork, NotificationUnitOfWork>();
        services.AddScoped<INotificationOutbox, NotificationOutbox>();

        services.Configure<RecipientOptions>(
            configuration.GetSection(RecipientOptions.SectionName));

        services.AddScoped<IRecipientDirectory, PlatformRecipientDirectory>();

        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<DispatchOptions>(configuration.GetSection(DispatchOptions.SectionName));

        // Every channel the Platform can deliver on. One line each.
        services.AddScoped<INotificationChannelProvider, InAppChannelProvider>();
        services.AddScoped<INotificationChannelProvider, EmailChannelProvider>();

        services.AddHostedService<NotificationDispatcher>();

        return services;
    }
}
