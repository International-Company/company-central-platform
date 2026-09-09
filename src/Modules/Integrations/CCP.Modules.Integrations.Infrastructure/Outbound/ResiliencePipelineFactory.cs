using System.Net;
using CCP.Modules.Integrations.Domain.Providers;
using Polly;
using Polly.CircuitBreaker;
using Polly.Registry;
using Polly.Retry;
using Polly.Timeout;

namespace CCP.Modules.Integrations.Infrastructure.Outbound;

/// <summary>
/// Builds the resilience pipeline for a provider, from that provider's own
/// settings.
/// <para>
/// <b>Four things in a fixed order, applied to every provider identically</b>
/// (§19.2). The order is not decoration — it decides what each one actually
/// protects:
/// </para>
/// <list type="number">
/// <item><b>Bulkhead</b> outermost, so a slow provider cannot occupy more than
/// its share of the Platform's threads. Inside the retry it would count each
/// attempt separately and the limit would mean something different for a
/// provider that retries.</item>
/// <item><b>Circuit breaker</b> next, so it sees the outcome of the retries and
/// opens on genuine, repeated failure rather than on the first blip.</item>
/// <item><b>Retry</b>, so each attempt gets its own fresh timeout.</item>
/// <item><b>Timeout</b> innermost, bounding one attempt. Outside the retry it
/// would bound the whole sequence, and the third attempt would inherit whatever
/// the first two left of the budget.</item>
/// </list>
/// <para>
/// Built once per provider and cached by the registry, because constructing a
/// circuit breaker per call would give it no memory at all — and a breaker with
/// no memory is an expensive way of doing nothing.
/// </para>
/// </summary>
public static class ResiliencePipelineFactory
{
    public static ResiliencePipeline For(
        ResiliencePipelineRegistry<string> registry, IntegrationProvider provider)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(provider);

        // Keyed on the settings as well as the provider, so changing a timeout
        // takes effect on the next call. Keyed on the code alone, an
        // administrator would edit the configuration and watch nothing happen
        // until the next deployment.
        string key = $"{provider.Code}:{provider.Timeout.TotalMilliseconds}"
                     + $":{provider.MaxRetries}:{provider.FailuresBeforeBreaking}"
                     + $":{provider.BreakDuration.TotalMilliseconds}:{provider.MaxConcurrentCalls}";

        return registry.GetOrAddPipeline(key, builder => Build(builder, provider));
    }

    private static void Build(ResiliencePipelineBuilder builder, IntegrationProvider provider)
    {
        builder
            // No queue. A caller waiting for a slot on a provider that is
            // already saturated is a thread held for an answer that is probably
            // a timeout anyway — which is the thing the bulkhead exists to
            // prevent.
            .AddConcurrencyLimiter(permitLimit: provider.MaxConcurrentCalls, queueLimit: 0)
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                // Polly counts a failure ratio over a window rather than
                // consecutive failures. One is expressed in terms of the other:
                // a low threshold with a minimum number of calls is the same
                // intent — do not open on one bad request, do open on a provider
                // that is genuinely down.
                FailureRatio = 0.5,
                MinimumThroughput = Math.Max(2, provider.FailuresBeforeBreaking),
                SamplingDuration = TimeSpan.FromSeconds(
                    Math.Max(30, provider.BreakDuration.TotalSeconds)),
                BreakDuration = provider.BreakDuration,
                ShouldHandle = BreakOn
            })
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = provider.MaxRetries,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(200),

                // Jitter, and it is not a nicety. Without it every caller whose
                // request failed at the same moment retries at the same moment,
                // and a provider coming back from an outage is met by the whole
                // backlog at once — which is how a recovery becomes a second
                // outage.
                UseJitter = true,
                ShouldHandle = RetryOn
            })
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = provider.Timeout
            });
    }

    /// <summary>
    /// What counts as a failure worth retrying and worth breaking on.
    /// <para>
    /// <b>Not every failure.</b> A 400 means the request was wrong, and sending
    /// it again unchanged will be wrong again — retrying it wastes time and adds
    /// load a struggling provider did not ask for. A 401 means the credential is
    /// wrong, which a retry cannot fix either.
    /// </para>
    /// <para>
    /// A 429 <i>is</i> retried, because the provider has explicitly said "later"
    /// — and the backoff with jitter is what makes that a polite answer rather
    /// than a faster loop.
    /// </para>
    /// </summary>
    private static ValueTask<bool> BreakOn(CircuitBreakerPredicateArguments<object> arguments)
        => ValueTask.FromResult(IsTransient(arguments.Outcome));

    private static ValueTask<bool> RetryOn(RetryPredicateArguments<object> arguments)
        => ValueTask.FromResult(IsTransient(arguments.Outcome));

    private static bool IsTransient(Outcome<object> outcome)
    {
        if (outcome.Exception is TimeoutRejectedException or HttpRequestException)
        {
            return true;
        }

        if (outcome.Result is not HttpResponseMessage response)
        {
            return false;
        }

        return response.StatusCode switch
        {
            HttpStatusCode.RequestTimeout => true,
            HttpStatusCode.TooManyRequests => true,
            >= HttpStatusCode.InternalServerError => true,
            _ => false
        };
    }
}
