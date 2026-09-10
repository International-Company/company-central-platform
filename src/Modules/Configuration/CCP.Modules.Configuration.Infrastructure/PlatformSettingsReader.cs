using CCP.Kernel.Application.Configuration;
using CCP.Modules.Configuration.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CCP.Modules.Configuration.Infrastructure;

/// <summary>
/// The kernel's settings seam, answered by the Configuration module.
/// <para>
/// The same arrangement as the audit trail: the kernel owns the contract so
/// every module can read a setting, and one module owns the behaviour. Nothing
/// that calls <see cref="IPlatformSettings"/> knows this class exists.
/// </para>
/// <para>
/// <b>A singleton over its own scope, because its callers are singletons.</b>
/// The outbox relay, the job journal and four background sweeps are all
/// singletons; a scoped reader could not be injected into any of them.
/// </para>
/// </summary>
public sealed class PlatformSettingsReader(
    IServiceScopeFactory scopeFactory,
    ILogger<PlatformSettingsReader> logger) : IPlatformSettings
{
    public Task<TimeSpan> GetDurationAsync(
        string key, TimeSpan fallback, CancellationToken cancellationToken = default)
        => ReadAsync(
            key,
            fallback,
            (reader, token) => reader.GetDurationAsync(key, fallback, token),
            cancellationToken);

    public Task<int> GetIntegerAsync(
        string key, int fallback, CancellationToken cancellationToken = default)
        => ReadAsync(
            key,
            fallback,
            (reader, token) => reader.GetIntegerAsync(key, fallback, token),
            cancellationToken);

    public Task<bool> GetBooleanAsync(
        string key, bool fallback, CancellationToken cancellationToken = default)
        => ReadAsync(
            key,
            fallback,
            (reader, token) => reader.GetBooleanAsync(key, fallback, token),
            cancellationToken);

    /// <summary>
    /// Reads one setting, and returns the caller's fallback if anything at all
    /// goes wrong.
    /// <para>
    /// <b>The catch is the point of this method.</b> Every caller is a
    /// background sweep whose fallback is the value it shipped with, and the
    /// failure being guarded against is the database being briefly unreachable.
    /// Throwing here would turn a transient blip into a failed sweep; worse, a
    /// reader that returned a default <c>TimeSpan</c> instead would hand a
    /// retention sweep a cutoff of zero, and a retention sweep with a cutoff of
    /// zero deletes everything.
    /// </para>
    /// <para>
    /// Logged at warning rather than swallowed silently, because a Platform
    /// quietly running on its compiled-in defaults is worth knowing about.
    /// </para>
    /// </summary>
    private async Task<T> ReadAsync<T>(
        string key,
        T fallback,
        Func<IConfigurationReader, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();

            var reader = scope.ServiceProvider.GetRequiredService<IConfigurationReader>();

            return await read(reader, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A setting being unreadable must not fail the work that wanted it.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                exception,
                "The setting {Key} could not be read. Falling back to the compiled-in value.",
                key);

            return fallback;
        }
    }
}
