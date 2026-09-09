using CCP.Modules.Documents.Application;
using CCP.Modules.Documents.Application.Abstractions;
using CCP.Modules.Documents.Infrastructure.Persistence;
using CCP.Modules.Documents.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Documents.Infrastructure;

/// <summary>
/// Registers the Documents module's infrastructure.
/// <para>
/// The interesting line is the storage choice: object storage when it has been
/// configured, a directory on disk when it has not. Nothing above this file
/// knows which one it got.
/// </para>
/// </summary>
public static class DocumentInfrastructureRegistration
{
    public static IServiceCollection AddDocumentInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<DocumentDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable(
                    "__ef_migrations_history", DocumentDbContext.SchemaName)));

        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IDocumentUnitOfWork, DocumentUnitOfWork>();
        services.AddScoped<IDocumentOutbox, DocumentOutbox>();

        services.Configure<DocumentOptions>(configuration.GetSection(DocumentOptions.SectionName));
        services.Configure<LocalStorageOptions>(
            configuration.GetSection(LocalStorageOptions.SectionName));
        services.Configure<S3StorageOptions>(configuration.GetSection(S3StorageOptions.SectionName));

        // The options object itself, so the Application layer can take its
        // settings without taking a dependency on the options machinery.
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<DocumentOptions>>().Value);

        AddStorage(services, configuration);

        // No scanner. The default says NotScanned rather than Clean, and that
        // verdict is recorded — so an audit of what was checked tells the truth.
        services.AddScoped<IDocumentScanner, NoOpDocumentScanner>();

        services.AddScoped<IAccessSubjectResolver, PlatformAccessSubjectResolver>();

        services.AddHostedService<PurgeSweep>();

        return services;
    }

    /// <summary>
    /// Picks the store, from what has been configured and nothing else.
    /// <para>
    /// Not from the environment name. "Production means S3" is a rule that
    /// works until the first production deployment without a bucket, which then
    /// fails at the first upload instead of at startup; and it makes testing the
    /// object-storage path locally impossible without lying about the
    /// environment.
    /// </para>
    /// </summary>
    private static void AddStorage(IServiceCollection services, IConfiguration configuration)
    {
        var s3 = new S3StorageOptions();
        configuration.GetSection(S3StorageOptions.SectionName).Bind(s3);

        if (s3.IsConfigured)
        {
            services.AddSingleton<IDocumentStorageProvider, S3StorageProvider>();
        }
        else
        {
            services.AddSingleton<IDocumentStorageProvider, LocalFileStorageProvider>();
        }
    }
}
