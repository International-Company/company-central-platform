using CCP.Kernel.Api.Observability;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CCP.Api.Host.Configuration;

/// <summary>
/// Traces and metrics, exported over OTLP (ADR-015).
/// <para>
/// <b>Instrumentation always; export only when somewhere has been configured to
/// send it.</b> The instrumentation is vendor-neutral and costs almost nothing
/// when nothing is listening, so it is unconditional — and moving to a different
/// backend later is an endpoint change rather than re-instrumenting a Platform.
/// </para>
/// <para>
/// No self-hosted stack. Deploying Prometheus, Grafana, Loki and Jaeger to watch
/// one application would produce monitoring infrastructure more complex than the
/// thing it monitors, which would itself need monitoring (§22.6).
/// </para>
/// </summary>
public static class ObservabilityRegistration
{
    /// <summary>
    /// Where telemetry is sent. Absent means instrument and export nothing,
    /// which is the correct behaviour for a developer machine.
    /// </summary>
    public const string ExporterEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    public static IServiceCollection AddPlatformObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        string serviceVersion)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<PlatformMetrics>();

        bool exports = !string.IsNullOrWhiteSpace(configuration[ExporterEndpointKey]);

        var telemetry = services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: serviceName,
                serviceVersion: serviceVersion));

        telemetry.WithTracing(tracing =>
        {
            tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    // Health probes arrive every few seconds forever and say
                    // nothing. Tracing them would bury the requests that matter
                    // and be paid for by the ingestion bill.
                    options.Filter = context =>
                        !context.Request.Path.StartsWithSegments("/health");

                    // The correlation id is written onto the span by the
                    // middleware that decides it, not here: that is where the
                    // value exists, and a second reader would be a second place
                    // to get it wrong.
                })
                .AddHttpClientInstrumentation()
                .AddSource(PlatformMetrics.MeterName)

                // Sampled rather than instrumented less. Turning off
                // instrumentation to save money leaves whole paths invisible;
                // sampling leaves every path visible and some requests
                // unrecorded, which is the far better trade when something is
                // wrong (§22 risks).
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(
                    configuration.GetValue("Observability:TraceSampleRatio", 1.0))));

            if (exports)
            {
                tracing.AddOtlpExporter();
            }
        });

        telemetry.WithMetrics(metrics =>
        {
            metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(PlatformMetrics.MeterName);

            if (exports)
            {
                metrics.AddOtlpExporter();
            }
        });

        return services;
    }
}
