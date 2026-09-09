using CCP.Kernel.Api.Modules;
using CCP.Kernel.Application.Modules;
using CCP.Modules.Notifications.Application;
using CCP.Modules.Notifications.Application.Sending;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Modules.Notifications.Api;

/// <summary>
/// The Notifications module's registration point (ARCHITECTURE.md §7.1).
/// <para>
/// <c>NotificationSender</c> is registered here because it is what other modules
/// call: Workflow, Identity and Security all need to say something to somebody,
/// and this is the only thing they touch.
/// </para>
/// </summary>
public sealed class NotificationsModule : IPlatformModule, IModuleEndpoints
{
    public string Name => "notifications";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<NotificationSender>();

        services.AddScoped<GetInboxHandler>();
        services.AddScoped<MarkReadHandler>();
        services.AddScoped<SearchNotificationsHandler>();
        services.AddScoped<GetTemplatesHandler>();
        services.AddScoped<SaveTemplateHandler>();
        services.AddScoped<GetPreferencesHandler>();
        services.AddScoped<SetPreferenceHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder versionGroup)
        => versionGroup.MapNotificationEndpoints();
}
