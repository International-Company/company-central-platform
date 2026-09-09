using CCP.Modules.Integrations.Domain.Providers;
using CCP.Modules.Integrations.Infrastructure.Outbound;
using Polly;
using Polly.Registry;

namespace CCP.Modules.Integrations.UnitTests;

/// <summary>
/// Building the pipeline for a provider.
/// <para>
/// These pin a defect the stub-server tests found: Polly validates
/// <c>MaxRetryAttempts</c> as at least one, so a provider configured <b>not to
/// retry</b> threw a validation error while its pipeline was being built — and
/// therefore failed every call rather than making one. Zero retries means no
/// retry strategy, not a retry strategy set to zero.
/// </para>
/// </summary>
public sealed class ResiliencePipelineTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private static IntegrationProvider AProvider(int maxRetries)
    {
        IntegrationProvider provider =
            IntegrationProvider.Create("acme", "Acme", "https://api.acme.test", Now).Value;

        provider.ConfigureResilience(
            TimeSpan.FromSeconds(5), maxRetries, 5, TimeSpan.FromSeconds(30), 8, Now);

        return provider;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public void APipelineIsBuiltForEveryRetryCountTheDomainAccepts(int maxRetries)
    {
        using var registry = new ResiliencePipelineRegistry<string>();

        Exception? thrown = Record.Exception(
            () => ResiliencePipelineFactory.For(registry, AProvider(maxRetries)));

        // The domain permits zero through five. Every one of those must produce
        // a pipeline, or the validation is a promise the infrastructure breaks.
        Assert.Null(thrown);
    }

    [Fact]
    public void TheSameSettingsReuseTheSamePipeline()
    {
        using var registry = new ResiliencePipelineRegistry<string>();

        ResiliencePipeline first = ResiliencePipelineFactory.For(registry, AProvider(2));
        ResiliencePipeline second = ResiliencePipelineFactory.For(registry, AProvider(2));

        // The circuit breaker's memory lives in the pipeline. One built per call
        // would forget every failure, which is an expensive way of doing
        // nothing.
        Assert.Same(first, second);
    }

    [Fact]
    public void ChangedSettingsProduceADifferentPipeline()
    {
        using var registry = new ResiliencePipelineRegistry<string>();

        ResiliencePipeline patient = ResiliencePipelineFactory.For(registry, AProvider(2));
        ResiliencePipeline impatient = ResiliencePipelineFactory.For(registry, AProvider(0));

        // Otherwise an administrator edits a timeout and watches nothing happen
        // until the next deployment.
        Assert.NotSame(patient, impatient);
    }
}
