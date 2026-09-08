using Microsoft.AspNetCore.Routing;

namespace CCP.Kernel.Api.Modules;

/// <summary>
/// A module's endpoint registration.
/// <para>
/// Kept separate from <c>IPlatformModule</c> so that the Application layer,
/// which declares service registration, never needs to reference ASP.NET Core
/// (ARCHITECTURE.md §6.1).
/// </para>
/// </summary>
public interface IModuleEndpoints
{
    /// <summary>
    /// Maps this module's endpoints onto the versioned API route group.
    /// </summary>
    /// <param name="versionGroup">The <c>/api/v1</c> group.</param>
    void MapEndpoints(IEndpointRouteBuilder versionGroup);
}
