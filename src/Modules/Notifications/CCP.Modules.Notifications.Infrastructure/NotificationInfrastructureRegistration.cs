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

        // What the Platform itself has to say. Registered here rather than in
        // the module's Api layer because the templates are infrastructure — they
        // are rows, not behaviour.
        services.AddSingleton<TemplateSeeder>();

        // The events this module turns into messages. Workflow does not know
        // notifications exist; these are the only things that connect them, and
        // adding a listener never changes the module that raised the event.
        services.AddScoped<
            CCP.Kernel.Application.Events.IIntegrationEventHandler<
                Modules.Workflow.Contracts.Events.WorkflowTaskAssignedEvent>,
            Listeners.TaskAssignedListener>();

        services.AddScoped<
            CCP.Kernel.Application.Events.IIntegrationEventHandler<
                Modules.Workflow.Contracts.Events.WorkflowTaskEscalatedEvent>,
            Listeners.TaskEscalatedListener>();

        services.AddScoped<
            CCP.Kernel.Application.Events.IIntegrationEventHandler<
                Modules.Workflow.Contracts.Events.WorkflowInstanceCompletedEvent>,
            Listeners.InstanceCompletedListener>();

        // Security. The reset listener closes the oldest debt in the project:
        // the token has been staged on the outbox since Phase 2 with nothing to
        // deliver it, so the flow existed, was tested, and could not complete.
        services.AddScoped<
            CCP.Kernel.Application.Events.IIntegrationEventHandler<
                Modules.Identity.Contracts.Events.PasswordResetRequestedEvent>,
            Listeners.PasswordResetRequestedListener>();

        services.AddScoped<
            CCP.Kernel.Application.Events.IIntegrationEventHandler<
                Modules.Identity.Contracts.Events.UserPasswordChangedEvent>,
            Listeners.PasswordChangedListener>();

        return services;
    }
}
