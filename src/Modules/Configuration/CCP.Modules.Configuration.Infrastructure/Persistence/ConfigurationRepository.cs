using CCP.Modules.Configuration.Application.Abstractions;
using CCP.Modules.Configuration.Domain.Flags;
using CCP.Modules.Configuration.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Caching.Memory;

namespace CCP.Modules.Configuration.Infrastructure.Persistence;

/// <summary>Reads and writes the configuration schema.</summary>
public sealed class ConfigurationRepository(ConfigurationDbContext dbContext)
    : IConfigurationRepository
{
    public async Task<SettingDefinition?> FindDefinitionAsync(
        string key, CancellationToken cancellationToken = default)
    {
        string normalised = key.Trim().ToLowerInvariant();

        return await dbContext.Definitions
            .FirstOrDefaultAsync(d => d.Key == normalised, cancellationToken);
    }

    public async Task<IReadOnlyList<SettingDefinition>> GetDefinitionsAsync(
        string? applicationCode, CancellationToken cancellationToken = default)
    {
        IQueryable<SettingDefinition> query = dbContext.Definitions.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(applicationCode))
        {
            string code = applicationCode.Trim().ToLowerInvariant();

            query = query.Where(d => d.ApplicationCode == code);
        }

        return await query.OrderBy(d => d.Key).ToListAsync(cancellationToken);
    }

    public void AddDefinition(SettingDefinition definition) =>
        dbContext.Definitions.Add(definition);

    public async Task<IReadOnlyList<SettingValue>> GetValuesAsync(
        string key, CancellationToken cancellationToken = default)
    {
        string normalised = key.Trim().ToLowerInvariant();

        return await dbContext.Values
            .AsNoTracking()
            .Where(v => v.Key == normalised)
            .ToListAsync(cancellationToken);
    }

    public async Task<SettingValue?> FindValueAsync(
        string key, SettingScope scope, Guid? scopeId, CancellationToken cancellationToken = default)
    {
        string normalised = key.Trim().ToLowerInvariant();

        return await dbContext.Values
            .FirstOrDefaultAsync(
                v => v.Key == normalised && v.Scope == scope && v.ScopeId == scopeId,
                cancellationToken);
    }

    public void AddValue(SettingValue value) => dbContext.Values.Add(value);

    public void RemoveValue(SettingValue value) => dbContext.Values.Remove(value);

    public void AddChange(SettingChange change) => dbContext.Changes.Add(change);

    public async Task<IReadOnlyList<SettingChange>> GetChangesAsync(
        string key, int limit, CancellationToken cancellationToken = default)
    {
        string normalised = key.Trim().ToLowerInvariant();

        return await dbContext.Changes
            .AsNoTracking()
            .Where(c => c.Key == normalised)
            .OrderByDescending(c => c.ChangedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<FeatureFlag?> FindFlagAsync(
        string key, CancellationToken cancellationToken = default)
    {
        string normalised = key.Trim().ToLowerInvariant();

        return await dbContext.Flags
            .FirstOrDefaultAsync(f => f.Key == normalised, cancellationToken);
    }

    public async Task<IReadOnlyList<FeatureFlag>> GetFlagsAsync(
        string? applicationCode, CancellationToken cancellationToken = default)
    {
        IQueryable<FeatureFlag> query = dbContext.Flags.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(applicationCode))
        {
            string code = applicationCode.Trim().ToLowerInvariant();

            query = query.Where(f => f.ApplicationCode == code);
        }

        return await query.OrderBy(f => f.Key).ToListAsync(cancellationToken);
    }

    public void AddFlag(FeatureFlag flag) => dbContext.Flags.Add(flag);

    /// <summary>
    /// The whole configuration in three queries.
    /// <para>
    /// Everything, because the alternative is a query per key on a path that
    /// runs whenever anything reads a setting. The table is small by
    /// construction: definitions are declared by developers, and values exist
    /// only where somebody overrode something.
    /// </para>
    /// </summary>
    public async Task<ConfigurationSnapshot> LoadSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        List<SettingDefinition> definitions =
            await dbContext.Definitions.AsNoTracking().ToListAsync(cancellationToken);

        List<SettingValue> values =
            await dbContext.Values.AsNoTracking().ToListAsync(cancellationToken);

        List<FeatureFlag> flags =
            await dbContext.Flags.AsNoTracking().ToListAsync(cancellationToken);

        ConfigurationVersionRow version =
            await dbContext.Version.AsNoTracking().FirstAsync(cancellationToken);

        return new ConfigurationSnapshot(
            definitions.ToDictionary(d => d.Key, StringComparer.Ordinal),
            values.GroupBy(v => v.Key, StringComparer.Ordinal)
                  .ToDictionary(
                      g => g.Key,
                      IReadOnlyList<SettingValue> (g) => [.. g],
                      StringComparer.Ordinal),
            flags.ToDictionary(f => f.Key, StringComparer.Ordinal),
            version.Version);
    }
}

/// <summary>Commits the Configuration module's changes.</summary>
public sealed class ConfigurationUnitOfWork(ConfigurationDbContext dbContext)
    : IConfigurationUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => dbContext.SaveChangesAsync(cancellationToken);
}

/// <summary>
/// The stamp that decides whether a cached snapshot is still good.
/// </summary>
public sealed class ConfigurationVersionStore(ConfigurationDbContext dbContext)
    : IConfigurationVersionStore
{
    public async Task<long> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        ConfigurationVersionRow row =
            await dbContext.Version.AsNoTracking().FirstAsync(cancellationToken);

        return row.Version;
    }

    /// <summary>
    /// Moves the stamp on.
    /// <para>
    /// A raw update rather than load-modify-save, so two instances changing
    /// settings at the same moment both move it rather than one overwriting the
    /// other's increment with a stale value.
    /// </para>
    /// </summary>
    public async Task BumpAsync(CancellationToken cancellationToken = default)
        => await dbContext.Version
            .ExecuteUpdateAsync(
                row => row
                    .SetProperty(r => r.Version, r => r.Version + 1)
                    .SetProperty(r => r.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);
}

/// <summary>
/// Holds the loaded configuration between requests.
/// <para>
/// One entry for the whole Platform rather than one per key: the snapshot is
/// loaded and discarded as a unit, and a per-key cache would need a per-key
/// invalidation that the version stamp already makes unnecessary.
/// </para>
/// </summary>
public sealed class ConfigurationCache(IMemoryCache cache) : IConfigurationCache
{
    private const string Key = "configuration:snapshot";

    public bool TryGet(out ConfigurationSnapshot? snapshot)
        => cache.TryGetValue(Key, out snapshot);

    public void Set(ConfigurationSnapshot snapshot)
        => cache.Set(Key, snapshot, new MemoryCacheEntryOptions
        {
            // Bounded like every other entry in this cache, and it only ever
            // costs a reload. The version stamp is what makes it correct; the
            // expiry only stops an idle process holding it forever.
            Size = 1,
            SlidingExpiration = TimeSpan.FromMinutes(30)
        });

    public void Clear() => cache.Remove(Key);
}

/// <summary>Builds the context for design-time tooling.</summary>
public sealed class ConfigurationDbContextFactory
    : IDesignTimeDbContextFactory<ConfigurationDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=ccp_design_time;Username=design;Password=design";

    public ConfigurationDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("CCP_ConnectionStrings__Platform")
            ?? DesignTimeConnectionString;

        DbContextOptions<ConfigurationDbContext> options =
            new DbContextOptionsBuilder<ConfigurationDbContext>()
                .UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable(
                        "__ef_migrations_history", ConfigurationDbContext.SchemaName))
                .Options;

        return new ConfigurationDbContext(options);
    }
}
