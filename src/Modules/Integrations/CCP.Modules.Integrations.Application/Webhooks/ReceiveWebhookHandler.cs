using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Integrations.Application.Abstractions;
using CCP.Modules.Integrations.Domain;
using CCP.Modules.Integrations.Domain.Providers;
using CCP.Modules.Integrations.Domain.Webhooks;
using Microsoft.Extensions.Logging;

namespace CCP.Modules.Integrations.Application.Webhooks;

/// <summary>
/// A webhook as it arrived: the provider it claims to be from, the raw bytes,
/// and the headers that are supposed to prove it.
/// </summary>
/// <param name="ProviderCode">Taken from the route.</param>
/// <param name="Signature">The presented signature.</param>
/// <param name="Timestamp">Unix seconds, as the sender stated them.</param>
/// <param name="Body">
/// The raw bytes, exactly as received. <b>Not a parsed object</b> — the
/// signature is over the bytes, and parsing then re-serialising would change
/// whitespace and key order and produce a different signature from the one the
/// provider computed.
/// </param>
public sealed record InboundWebhook(
    string ProviderCode,
    string? Signature,
    long Timestamp,
    ReadOnlyMemory<byte> Body);

/// <summary>
/// Accepts an inbound webhook, or refuses it.
/// <para>
/// <b>An inbound webhook is untrusted input from the internet</b> (§19.5). The
/// URL is guessable and often published; anybody can post to it. Three things
/// have to be true before the Platform believes a word of it: the provider must
/// have a signing secret, the signature must match the body, and the request
/// must not have been seen before.
/// </para>
/// <para>
/// Every refusal returns the same error. Telling a sender that the signature was
/// wrong but the timestamp was fine tells an attacker which half to work on.
/// The Platform's own log records which check failed, because that distinction
/// matters enormously to an operator and not at all to the caller.
/// </para>
/// </summary>
public sealed class ReceiveWebhookHandler(
    IIntegrationRepository repository,
    ISecretResolver secrets,
    IIntegrationUnitOfWork unitOfWork,
    IntegrationOptions options,
    IClock clock,
    ILogger<ReceiveWebhookHandler> logger)
{
    /// <summary>
    /// What the accepted body is handed back as.
    /// </summary>
    /// <param name="ProviderCode">Who it is from, now verified.</param>
    /// <param name="Body">The bytes, unchanged.</param>
    public sealed record AcceptedWebhook(string ProviderCode, ReadOnlyMemory<byte> Body);

    public async Task<Result<AcceptedWebhook>> HandleAsync(
        InboundWebhook webhook, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(webhook);

        if (webhook.Body.Length > options.MaxWebhookBodyBytes)
        {
            return Result.Failure<AcceptedWebhook>(IntegrationErrors.WebhookTooLarge);
        }

        IntegrationProvider? provider =
            await repository.FindProviderByCodeAsync(webhook.ProviderCode, cancellationToken);

        if (provider is null || provider.CredentialReference is null)
        {
            // A provider with no credential reference has no signing secret, so
            // nothing it received could be verified. Refused rather than
            // accepted unverified, which is the failure this whole handler
            // exists to prevent.
            return Result.Failure<AcceptedWebhook>(IntegrationErrors.WebhookNotConfigured);
        }

        if (!provider.IsEnabled)
        {
            return Result.Failure<AcceptedWebhook>(IntegrationErrors.ProviderDisabled);
        }

        string? secret = await secrets.ResolveAsync(provider.CredentialReference, cancellationToken);

        if (secret is null)
        {
            return Result.Failure<AcceptedWebhook>(IntegrationErrors.WebhookNotConfigured);
        }

        WebhookSignature.Verdict verdict = WebhookSignature.Verify(
            secret,
            webhook.Signature,
            webhook.Timestamp,
            webhook.Body.Span,
            clock.UtcNow,
            options.WebhookTolerance);

        if (verdict != WebhookSignature.Verdict.Valid)
        {
            return Refuse(provider.Code, verdict);
        }

        // Authentic, recent — and possibly the second copy. A correctly signed
        // request that arrives twice is authentic both times; the signature
        // proves who sent it and says nothing about whether it has been acted
        // on already.
        if (await repository.HasSeenWebhookAsync(
                provider.Code, webhook.Signature!, cancellationToken))
        {
            return Refuse(provider.Code, WebhookSignature.Verdict.Replayed);
        }

        repository.AddWebhookReceipt(
            WebhookReceipt.Record(provider.Code, webhook.Signature!, clock.UtcNow));

        // Committed before the caller is told yes. A receipt written after the
        // work would leave a window in which the same webhook could be accepted
        // twice — and the unique index on the receipt is what settles the race
        // when two copies arrive at the same instant.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new AcceptedWebhook(provider.Code, webhook.Body));
    }

    private Result<AcceptedWebhook> Refuse(string providerCode, WebhookSignature.Verdict verdict)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning(
                "Webhook from {Provider} refused: {Verdict}.", providerCode, verdict);
        }

        return Result.Failure<AcceptedWebhook>(IntegrationErrors.WebhookRejected);
    }
}
