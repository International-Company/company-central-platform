using CCP.Kernel.Application.Configuration;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Configuration.Application.Abstractions;
using CCP.Modules.Configuration.Domain.Settings;
using Microsoft.Extensions.Logging;

namespace CCP.Modules.Configuration.Infrastructure;

/// <summary>
/// Declares the Platform's own operational settings.
/// <para>
/// A setting must be declared before it can be set — that is what gives it a
/// type, bounds and a place on the Configuration screen. Without this, an
/// administrator wanting to change the document deletion grace period would have
/// to declare it through the API first, guessing the key the Platform reads,
/// which is a worse experience than the deployment it replaces.
/// </para>
/// <para>
/// <b>The defaults come from the caller, not from here.</b> The host passes the
/// values the Platform actually shipped with, so the declared default and the
/// compiled-in fallback are the same number by construction rather than by two
/// people remembering. A screen that showed a default the code does not use
/// would be worse than no screen.
/// </para>
/// <para>
/// <b>Idempotent, and it never overwrites.</b> It runs on every start; a setting
/// that already exists is left exactly as it is, because by then an
/// administrator may have changed its default deliberately and a seeder that
/// reset it every deployment would be the most confusing bug in the Platform.
/// </para>
/// </summary>
public sealed class PlatformSettingSeeder(
    IConfigurationRepository repository,
    IConfigurationUnitOfWork unitOfWork,
    IConfigurationVersionStore versionStore,
    IClock clock,
    ILogger<PlatformSettingSeeder> logger)
{
    public async Task SeedAsync(
        PlatformSettingDefaults defaults, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(defaults);

        DateTimeOffset now = clock.UtcNow;

        (string Key, string Description, TimeSpan Default)[] durations =
        [
            (PlatformSettingKeys.OutboxProcessedRetention,
                "How long a delivered event is kept before the sweep removes it. "
                + "Dead-lettered events are never removed, whatever this says.",
                defaults.OutboxProcessedRetention),

            (PlatformSettingKeys.JobHistoryRetention,
                "How long a background job run is kept in the Operations history.",
                defaults.JobHistoryRetention),

            (PlatformSettingKeys.IntegrationCallLogRetention,
                "How long an outbound call log entry is kept. The payloads in it "
                + "were already redacted before storage.",
                defaults.IntegrationCallLogRetention),

            (PlatformSettingKeys.DocumentDeletionGrace,
                "How long a document marked for deletion waits before its content "
                + "is destroyed. This one is irreversible when it elapses.",
                defaults.DocumentDeletionGrace),
        ];

        int declared = 0;

        foreach ((string key, string description, TimeSpan value) in durations)
        {
            if (await repository.FindDefinitionAsync(key, cancellationToken) is not null)
            {
                continue;
            }

            Result<SettingDefinition> definition = SettingDefinition.Declare(
                key,
                PlatformSettingKeys.Namespace,
                SettingValueType.Duration,
                description,
                value.ToString(),
                now);

            if (definition.IsFailure)
            {
                // Logged rather than thrown. A setting that failed to declare
                // leaves its caller on the compiled-in default, which is the
                // behaviour the Platform had before any of this existed -- and
                // that is a far better outcome than refusing to start.
                logger.LogError(
                    "The Platform setting {Key} could not be declared: {Error}",
                    key,
                    definition.Errors[0].Code);

                continue;
            }

            repository.AddDefinition(definition.Value);
            declared++;
        }

        if (declared == 0)
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The snapshot is version-stamped, so a declaration written here is
        // invisible to every reader until the stamp moves.
        await versionStore.BumpAsync(cancellationToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Declared {Count} Platform setting(s).", declared);
        }
    }
}

/// <summary>
/// The values the Platform shipped with, passed in by the host.
/// <para>
/// Supplied rather than hard-coded so the declared default and the fallback each
/// caller passes are the same number by construction. Two people remembering to
/// keep them in step is exactly how a screen ends up showing a default nothing
/// uses.
/// </para>
/// </summary>
public sealed record PlatformSettingDefaults(
    TimeSpan OutboxProcessedRetention,
    TimeSpan JobHistoryRetention,
    TimeSpan IntegrationCallLogRetention,
    TimeSpan DocumentDeletionGrace);
