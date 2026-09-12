using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using CCP.Kernel.Application.Jobs;
using CCP.Kernel.Primitives;
using CCP.Modules.Integrations.Application;
using CCP.Modules.Integrations.Application.Abstractions;
using CCP.Modules.Integrations.Domain.Webhooks;
using CCP.Modules.Integrations.Infrastructure.Outbound;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CCP.Modules.Integrations.Infrastructure.Webhooks;

/// <summary>
/// Posts queued events to the applications that asked for them.
/// <para>
/// <b>Every post goes out through the same door as every other outbound
/// call</b> — the named client whose socket is opened by the guard, so a
/// subscription's address is checked against the allow-list, checked for a
/// private address, and connected to the address that was checked. That is what
/// keeps a subscription from being an SSRF primitive, and it is the reason this
/// capability waited for the phase that owns outbound calls.
/// </para>
/// <para>
/// Retries are here rather than in a resilience pipeline, and the difference
/// matters: a pipeline retries inside one request, holding a thread. A queued
/// delivery with a next-attempt time survives a deployment, a restart and an
/// outage measured in hours, which is the timescale a subscriber's endpoint
/// actually comes back on.
/// </para>
/// </summary>
public sealed class WebhookDeliverySweep(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IntegrationOptions options,
    IClock clock,
    JobRunner jobs,
    ILogger<WebhookDeliverySweep> logger) : BackgroundService
{
    /// <summary>The name this sweep is known by in the job history.</summary>
    public const string JobName = "integrations.webhook-delivery";

    /// <summary>
    /// Names the signature scheme in the header itself.
    /// <para>
    /// So that changing it later is a version bump the receiver can branch on
    /// rather than a silent change of meaning — the same reasoning that put a
    /// version in the MFA key's stored form.
    /// </para>
    /// </summary>
    private const string SignatureVersion = "v1";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.WebhookPollInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await jobs.RunAsync(JobName, SweepAsync, stoppingToken);
        }
    }

    internal async Task<string?> SweepAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IIntegrationRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IIntegrationUnitOfWork>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretResolver>();

        DateTimeOffset now = clock.UtcNow;

        IReadOnlyList<WebhookDelivery> due = await repository.ClaimDueDeliveriesAsync(
            now, options.WebhookBatchSize, cancellationToken);

        if (due.Count == 0)
        {
            return null;
        }

        int delivered = 0;
        int failed = 0;
        int suspended = 0;

        foreach (WebhookDelivery delivery in due)
        {
            WebhookSubscription? subscription =
                await repository.FindSubscriptionAsync(delivery.SubscriptionId, cancellationToken);

            if (subscription is null || !subscription.IsLive)
            {
                // The subscription went away or was switched off while this was
                // queued. Abandoned rather than retried for ever: nobody is
                // waiting for it, and the row stays so that somebody asking
                // "what happened to my events" gets an answer.
                delivery.RecordFailure(
                    null, "The subscription is no longer live.", delivery.Attempts + 1,
                    TimeSpan.Zero, now);

                failed++;

                continue;
            }

            (bool ok, int? status, string? error) =
                await PostAsync(subscription, delivery, secrets, cancellationToken);

            if (ok)
            {
                delivery.RecordSuccess(status!.Value, now);
                subscription.RecordSuccess(now);
                delivered++;

                continue;
            }

            failed++;

            delivery.RecordFailure(
                status, error ?? "No response.", options.WebhookMaximumAttempts,
                BackoffFor(delivery.Attempts), now);

            if (subscription.RecordFailure(
                    options.WebhookFailuresBeforeSuspending,
                    $"{options.WebhookFailuresBeforeSuspending} consecutive failures.", now))
            {
                suspended++;

                // Loud, because a suspended subscription is a business system
                // that has silently stopped hearing about anything. The row says
                // so too, but a log line is what an alert can be built on.
                logger.LogWarning(
                    "Webhook subscription {Subscription} to {Endpoint} was suspended after "
                    + "{Failures} consecutive failures.",
                    subscription.Name, subscription.Endpoint, subscription.ConsecutiveFailures);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return suspended == 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Delivered {delivered}, failed {failed}.")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Delivered {delivered}, failed {failed}, suspended {suspended} subscription(s).");
    }

    /// <summary>
    /// One attempt, signed.
    /// <para>
    /// The signature covers a timestamp and the exact bytes of the body, using
    /// the same scheme the Platform requires of inbound webhooks. Symmetry is
    /// the point: one implementation to get right, one to explain, and a
    /// receiver can verify with the code this repository already documents.
    /// </para>
    /// </summary>
    private async Task<(bool Ok, int? Status, string? Error)> PostAsync(
        WebhookSubscription subscription,
        WebhookDelivery delivery,
        ISecretResolver secrets,
        CancellationToken cancellationToken)
    {
        string? secret = await secrets.ResolveAsync(subscription.SecretReference, cancellationToken);

        if (secret is null)
        {
            // Not sent unsigned. A webhook the receiver cannot authenticate is a
            // message anybody could have forged, and sending one because the
            // secret store was briefly unavailable would teach receivers to
            // accept unsigned messages.
            return (false, null, $"The secret '{subscription.SecretReference}' could not be resolved.");
        }

        byte[] body = Encoding.UTF8.GetBytes(delivery.Payload);
        long timestamp = clock.UtcNow.ToUnixTimeSeconds();

        using var message = new HttpRequestMessage(HttpMethod.Post, subscription.Endpoint)
        {
            Content = new ByteArrayContent(body)
        };

        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };

        message.Headers.TryAddWithoutValidation(
            "X-CCP-Signature",
            $"{SignatureVersion}={WebhookSignature.Compute(secret, timestamp, body)}");

        message.Headers.TryAddWithoutValidation(
            "X-CCP-Timestamp", timestamp.ToString(CultureInfo.InvariantCulture));

        // So a receiver can recognise a repeat. Delivery is at-least-once: a
        // response lost on the way back is indistinguishable from one that never
        // arrived, so the Platform tries again and the receiver needs something
        // to deduplicate on.
        message.Headers.TryAddWithoutValidation("X-CCP-Event-Id", delivery.EventId.ToString());
        message.Headers.TryAddWithoutValidation("X-CCP-Event-Type", delivery.EventType);
        message.Headers.TryAddWithoutValidation(
            "X-CCP-Attempt", (delivery.Attempts + 1).ToString(CultureInfo.InvariantCulture));

        HttpClient client = httpClientFactory.CreateClient(HttpIntegrationConnector.HttpClientName);

        client.Timeout = options.WebhookTimeout;

        try
        {
            using HttpResponseMessage response = await client.SendAsync(message, cancellationToken);

            return response.IsSuccessStatusCode
                ? (true, (int)response.StatusCode, null)
                : (false, (int)response.StatusCode, $"The endpoint answered {(int)response.StatusCode}.");
        }
        catch (Exception exception) when (OutboundRefusedException.Find(exception) is not null)
        {
            OutboundRefusedException refused = OutboundRefusedException.Find(exception)!;

            // Refused by the outbound policy rather than by the subscriber. The
            // address was checked when the subscription was registered, so this
            // means the allow-list changed or the name now resolves somewhere
            // else -- both of which somebody needs to read, not a retry.
            return (false, null, $"Refused by the outbound policy: {refused.Verdict}.");
        }
        catch (HttpRequestException exception)
        {
            return (false, null, exception.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, null, $"No answer within {options.WebhookTimeout.TotalSeconds:0.#}s.");
        }
    }

    /// <summary>
    /// Exponential, with jitter.
    /// <para>
    /// Without the jitter, every delivery that failed during an outage retries
    /// at the same instant when the endpoint comes back — which is how a
    /// recovery becomes a second outage, this time caused by us.
    /// </para>
    /// </summary>
    private TimeSpan BackoffFor(int attempts)
    {
        double seconds =
            options.WebhookInitialBackoff.TotalSeconds * Math.Pow(2, Math.Min(attempts, 8));

        double jitter = Random.Shared.NextDouble() * 0.3 * seconds;

        return TimeSpan.FromSeconds(seconds + jitter);
    }
}
