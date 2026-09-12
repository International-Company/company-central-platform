using System.Globalization;
using System.Net;
using System.Text;
using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Primitives;
using CCP.Modules.Integrations.Application.Abstractions;
using CCP.Modules.Integrations.Domain.Logging;
using CCP.Modules.Integrations.Domain.Outbound;
using CCP.Modules.Integrations.Domain.Providers;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Registry;
using Polly.Timeout;

namespace CCP.Modules.Integrations.Infrastructure.Outbound;

/// <summary>
/// The one door to the outside world (§19.1).
/// <para>
/// Every outbound call passes through here and gets the same treatment: the
/// address is checked, the credential is resolved from a reference, the request
/// is sent under a resilience pipeline built from the provider's own settings,
/// and both halves are written to the call log with the provider's declared
/// fields blanked.
/// </para>
/// <para>
/// <b>Nothing here understands what is being sent.</b> The body is a string this
/// class transports; the layer that interpreted it would be a business rule
/// living in the Platform, which is the one thing this project exists not to
/// have.
/// </para>
/// </summary>
public sealed class HttpIntegrationConnector(
    IHttpClientFactory httpClientFactory,
    IIntegrationRepository repository,
    IIntegrationUnitOfWork unitOfWork,
    IOutboundGuard guard,
    ISecretResolver secrets,
    ResiliencePipelineRegistry<string> pipelines,
    IRequestContext requestContext,
    IClock clock,
    ILogger<HttpIntegrationConnector> logger) : IIntegrationConnector
{
    /// <summary>The named client, so timeouts and handlers are configured once.</summary>
    public const string HttpClientName = "ccp-integrations";

    public async Task<IntegrationResponse> SendAsync(
        IntegrationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset now = clock.UtcNow;

        IntegrationProvider? provider =
            await repository.FindProviderByCodeAsync(request.ProviderCode, cancellationToken);

        if (provider is null)
        {
            // Nothing to log against: the log is keyed on a provider, and a row
            // for one that does not exist would have nowhere to live.
            return new IntegrationResponse(
                null, null, CallOutcome.Failed, 0, Guid.Empty);
        }

        IntegrationEndpoint? endpoint = provider.FindEndpoint(request.EndpointKey);

        if (endpoint is null)
        {
            return new IntegrationResponse(null, null, CallOutcome.Failed, 0, Guid.Empty);
        }

        var redaction = provider.RedactionPolicy;

        // Redacted before the row is created, not before it is read. Storing the
        // real payload and hiding it at read time leaves the secret in the
        // database, where the next export and the next backup will find it.
        IntegrationCallLog log = IntegrationCallLog.Start(
            provider.Id,
            provider.Code,
            endpoint.Key,
            endpoint.Method,
            endpoint.PathTemplate,
            requestContext.CorrelationId,
            PayloadRedactor.Redact(request.Body, redaction),
            now);

        repository.AddCallLog(log);

        if (!provider.IsEnabled)
        {
            return await FinishAsync(
                log, CallOutcome.Blocked, "the provider is switched off", 0, cancellationToken);
        }

        CCP.Kernel.Results.Result<string> path = endpoint.BuildPath(request.PathArguments);

        if (path.IsFailure)
        {
            return await FinishAsync(
                log, CallOutcome.Failed, path.Error.Message, 0, cancellationToken);
        }

        Uri destination = BuildUri(provider, path.Value, request.Query);

        OutboundHostPolicy.Verdict verdict = await guard.InspectAsync(destination, cancellationToken);

        if (verdict != OutboundHostPolicy.Verdict.Allowed)
        {
            // Refused before anything is sent and before the credential is
            // resolved — so a misconfigured provider cannot leak a secret to a
            // host it was never allowed to reach.
            return await FinishAsync(
                log, CallOutcome.Blocked, verdict.ToString(), 0, cancellationToken);
        }

        string? credential = null;

        if (provider.CredentialReference is { } reference)
        {
            credential = await secrets.ResolveAsync(reference, cancellationToken);

            if (credential is null)
            {
                return await FinishAsync(
                    log, CallOutcome.Failed,
                    "the provider's credential could not be resolved", 0, cancellationToken);
            }
        }

        return await ExecuteAsync(
            provider, endpoint, destination, request, credential, redaction, log, cancellationToken);
    }

    private async Task<IntegrationResponse> ExecuteAsync(
        IntegrationProvider provider,
        IntegrationEndpoint endpoint,
        Uri destination,
        IntegrationRequest request,
        string? credential,
        IReadOnlyList<string> redaction,
        IntegrationCallLog log,
        CancellationToken cancellationToken)
    {
        ResiliencePipeline pipeline = ResiliencePipelineFactory.For(pipelines, provider);

        int attempts = 0;

        try
        {
            HttpResponseMessage response = await pipeline.ExecuteAsync(
                async token =>
                {
                    attempts++;

                    return await SendOnceAsync(
                        endpoint, destination, request, credential, token);
                },
                cancellationToken);

            using (response)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken);

                log.Complete(
                    (int)response.StatusCode,
                    PayloadRedactor.Redact(body, redaction),
                    attempts,
                    clock.UtcNow);

                await unitOfWork.SaveChangesAsync(cancellationToken);

                return new IntegrationResponse(
                    (int)response.StatusCode, body, log.Outcome, attempts, log.Id);
            }
        }
        catch (BrokenCircuitException)
        {
            // Not an error in this call. The provider is known to be failing and
            // the Platform is refusing to spend a thread finding out again —
            // logged, because "we did not call them" answers a question that an
            // absence of rows does not.
            return await FinishAsync(
                log, CallOutcome.CircuitOpen,
                "the circuit is open for this provider", attempts, cancellationToken);
        }
        catch (TimeoutRejectedException)
        {
            return await FinishAsync(
                log, CallOutcome.TimedOut,
                $"no answer within {provider.Timeout.TotalSeconds:0.#}s", attempts, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller gave up, not the provider. Recorded so the row does not
            // sit at Pending forever, and rethrown because the caller's
            // cancellation is not this layer's to swallow.
            await FinishAsync(
                log, CallOutcome.Failed, "the caller cancelled", attempts, CancellationToken.None);

            throw;
        }
        catch (Exception exception) when (OutboundRefusedException.Find(exception) is not null)
        {
            // Refused at the socket by the guard, not a failure of the provider.
            // Logged as blocked for the same reason the pre-flight refusal is:
            // "we would not connect" and "they did not answer" are different
            // answers to the question an operator is asking.
            OutboundRefusedException refused = OutboundRefusedException.Find(exception)!;

            return await FinishAsync(
                log, CallOutcome.Blocked, refused.Verdict.ToString(), attempts, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            return await FinishAsync(
                log, CallOutcome.Failed, exception.Message, attempts, cancellationToken);
        }
    }

    /// <summary>
    /// One attempt. Everything about retrying is the pipeline's business.
    /// </summary>
    private async Task<HttpResponseMessage> SendOnceAsync(
        IntegrationEndpoint endpoint,
        Uri destination,
        IntegrationRequest request,
        string? credential,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(new HttpMethod(endpoint.Method), destination);

        if (request.Body is not null)
        {
            message.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
        }

        if (request.Headers is not null)
        {
            foreach ((string name, string value) in request.Headers)
            {
                // Authorization is not accepted from a caller. The credential
                // comes from the provider's reference, so a caller can neither
                // attach one of its own nor discover the one that is used.
                if (!string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase))
                {
                    message.Headers.TryAddWithoutValidation(name, value);
                }
            }
        }

        if (credential is not null)
        {
            message.Headers.TryAddWithoutValidation("Authorization", $"Bearer {credential}");
        }

        HttpClient client = httpClientFactory.CreateClient(HttpClientName);

        return await client.SendAsync(
            message, HttpCompletionOption.ResponseContentRead, cancellationToken);
    }

    private async Task<IntegrationResponse> FinishAsync(
        IntegrationCallLog log,
        CallOutcome outcome,
        string reason,
        int attempts,
        CancellationToken cancellationToken)
    {
        log.Fail(outcome, reason, attempts, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning(
                "Integration call to {Provider}/{Endpoint} ended as {Outcome}: {Reason}.",
                log.ProviderCode, log.EndpointKey, outcome, reason);
        }

        return new IntegrationResponse(null, null, outcome, attempts, log.Id);
    }

    /// <summary>
    /// Joins the provider's base address to the endpoint's path.
    /// <para>
    /// The base address is authoritative: a path is relative and validated as
    /// such when the endpoint is created, so this cannot produce a URL pointing
    /// at another host.
    /// </para>
    /// </summary>
    private static Uri BuildUri(
        IntegrationProvider provider, string path, IReadOnlyDictionary<string, string>? query)
    {
        var baseAddress = new Uri(provider.BaseAddress, UriKind.Absolute);
        var builder = new UriBuilder(new Uri(baseAddress, path));

        if (query is { Count: > 0 })
        {
            builder.Query = string.Join(
                '&',
                query.Select(pair => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}")));
        }

        return builder.Uri;
    }
}
