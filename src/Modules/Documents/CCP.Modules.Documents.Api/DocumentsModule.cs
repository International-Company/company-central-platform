using CCP.Kernel.Api.Modules;
using CCP.Kernel.Api.Security;
using CCP.Kernel.Application.Modules;
using CCP.Modules.Documents.Application;
using CCP.Modules.Documents.Application.Abstractions;
using CCP.Modules.Documents.Application.Access;
using CCP.Modules.Documents.Application.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Modules.Documents.Api;

/// <summary>
/// The Documents module's registration point (ARCHITECTURE.md §7.1).
/// </summary>
public sealed class DocumentsModule : IPlatformModule, IModuleEndpoints
{
    public string Name => "documents";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<DocumentContentService>();
        services.AddScoped<DocumentAccessGuard>();
        services.AddScoped<ICallerScope, HttpCallerScope>();

        services.AddScoped<UploadDocumentHandler>();
        services.AddScoped<AddVersionHandler>();
        services.AddScoped<UpdateDocumentHandler>();
        services.AddScoped<DeleteDocumentHandler>();
        services.AddScoped<RestoreDocumentHandler>();

        services.AddScoped<GetDocumentHandler>();
        services.AddScoped<SearchDocumentsHandler>();
        services.AddScoped<PrepareDownloadHandler>();
        services.AddScoped<OpenDocumentContentHandler>();
        services.AddScoped<GetAccessLogHandler>();

        services.AddScoped<GrantAccessHandler>();
        services.AddScoped<RevokeAccessHandler>();
        services.AddScoped<GetAccessRulesHandler>();

        services.AddScoped<LinkDocumentHandler>();
        services.AddScoped<UnlinkDocumentHandler>();
        services.AddScoped<GetLinksHandler>();
        services.AddScoped<GetLinkedDocumentsHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder versionGroup)
        => versionGroup.MapDocumentEndpoints();
}

/// <summary>
/// Reads the kernel's scope filter and reports the one thing this module needs.
/// <para>
/// The adapter lives here because <c>ScopeFilter</c> is an API-layer type and
/// the handlers are not allowed to see it. Everything below this line knows only
/// "does this caller's grant reach the whole company".
/// </para>
/// </summary>
public sealed class HttpCallerScope(IHttpContextAccessor accessor) : ICallerScope
{
    /// <summary>
    /// True only for a caller whose permission is granted company-wide.
    /// <para>
    /// A missing filter reads as false, which is the safe direction: the kernel
    /// already defaults an unattached filter to the most restrictive one, and a
    /// wiring mistake should cost an administrator a click rather than open
    /// every document in the company.
    /// </para>
    /// </summary>
    public bool ReachesWholeCompany =>
        accessor.HttpContext?.GetScopeFilter().Kind == ScopeFilterKind.All;
}
