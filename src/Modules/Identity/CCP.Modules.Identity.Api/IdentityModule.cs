using CCP.Kernel.Api.Modules;
using CCP.Kernel.Application.Modules;
using CCP.Modules.Identity.Application.Passwords;
using CCP.Modules.Identity.Application.Users;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Modules.Identity.Api;

/// <summary>
/// The Identity module's registration point (ARCHITECTURE.md §7.1).
/// <para>
/// Services that need infrastructure types — the DbContext, the password
/// hasher, the token services — are registered by the Infrastructure layer's own
/// extension method, which the host calls. This class registers only what the
/// Api and Application layers own, so the Api project never has to reference
/// Infrastructure and the §6.1 dependency rule holds.
/// </para>
/// </summary>
public sealed class IdentityModule : IPlatformModule, IModuleEndpoints
{
    public string Name => "identity";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Application handlers. Plain classes, resolved by the container — no
        // mediator, per ADR-002.
        services.AddScoped<Application.Authentication.SignInHandler>();
        services.AddScoped<Application.Authentication.RefreshTokenHandler>();
        services.AddScoped<Application.Authentication.SignOutHandler>();

        // The single place a password is set, so policy, breach screening,
        // history and session revocation cannot be forgotten by a caller.
        services.AddScoped<PasswordSetter>();
        services.AddScoped<ChangePasswordHandler>();
        services.AddScoped<RequestPasswordResetHandler>();
        services.AddScoped<ResetPasswordHandler>();

        services.AddScoped<CreateUserHandler>();
        services.AddScoped<UpdateUserHandler>();
        services.AddScoped<ChangeUserStatusHandler>();
        services.AddScoped<SearchUsersHandler>();
        services.AddScoped<GetUserHandler>();
        services.AddScoped<GetMySessionsHandler>();
        services.AddScoped<GetMyLoginHistoryHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder versionGroup)
    {
        versionGroup.MapAuthenticationEndpoints();
        versionGroup.MapPasswordEndpoints();
        versionGroup.MapUserEndpoints();
        versionGroup.MapJwksEndpoints();
    }
}
