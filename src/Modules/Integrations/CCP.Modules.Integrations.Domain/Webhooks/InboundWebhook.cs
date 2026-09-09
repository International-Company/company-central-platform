using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;

namespace CCP.Modules.Integrations.Domain.Webhooks;

/// <summary>
/// Verifies that an inbound webhook really came from the provider it claims to.
/// <para>
/// <b>An inbound webhook is untrusted input from the internet</b> (§19.5). The
/// URL is usually guessable and often published; anybody can post to it. The
/// signature is the only thing separating "our payment provider says this
/// payment succeeded" from "somebody on the internet says our payment
/// succeeded".
/// </para>
/// </summary>
public static class WebhookSignature
{
    /// <summary>
    /// How far a timestamp may be from now.
    /// <para>
    /// Five minutes. Long enough for clock drift and a slow network, short
    /// enough that a captured request is worthless by the time it is replayed.
    /// A signature with no timestamp is valid forever, which makes one captured
    /// request a permanent ability to repeat it.
    /// </para>
    /// </summary>
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    /// <summary>Why a webhook was refused.</summary>
    public enum Verdict
    {
        Valid = 0,

        /// <summary>No signature header at all.</summary>
        Missing = 1,

        /// <summary>The signature does not match the body.</summary>
        Mismatch = 2,

        /// <summary>Outside the tolerance window — old, or from a future clock.</summary>
        StaleTimestamp = 3,

        /// <summary>Correctly signed, and seen before.</summary>
        Replayed = 4
    }

    /// <summary>
    /// Computes the expected signature over the timestamp and the raw body.
    /// <para>
    /// <b>The timestamp is inside the signed material, not beside it.</b> If it
    /// were only a header, an attacker replaying a captured request would simply
    /// change it — the tolerance window would then be checked against a value
    /// they control, which is no check at all.
    /// </para>
    /// <para>
    /// The <i>raw</i> body, byte for byte, is what gets signed. Parsing and
    /// re-serialising first would change whitespace and key order and produce a
    /// different signature from the one the provider computed.
    /// </para>
    /// </summary>
    public static string Compute(string secret, long timestamp, ReadOnlySpan<byte> body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        byte[] prefix = Encoding.UTF8.GetBytes(
            timestamp.ToString(CultureInfo.InvariantCulture) + ".");

        byte[] material = new byte[prefix.Length + body.Length];

        prefix.CopyTo(material, 0);
        body.CopyTo(material.AsSpan(prefix.Length));

        return Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), material));
    }

    /// <summary>
    /// Checks a presented signature and its timestamp.
    /// <para>
    /// Replay detection is the caller's, because it needs storage: this decides
    /// whether the request is authentic and recent, and the caller decides
    /// whether it has been seen. Both are required — a correctly signed request
    /// inside the window is still a replay if it arrived twice.
    /// </para>
    /// </summary>
    public static Verdict Verify(
        string secret,
        string? presentedSignature,
        long timestamp,
        ReadOnlySpan<byte> body,
        DateTimeOffset now,
        TimeSpan? tolerance = null)
    {
        if (string.IsNullOrWhiteSpace(presentedSignature))
        {
            return Verdict.Missing;
        }

        TimeSpan window = tolerance ?? DefaultTolerance;
        DateTimeOffset sentAt = DateTimeOffset.FromUnixTimeSeconds(timestamp);

        // Both directions. A timestamp far in the future is either a broken
        // clock or somebody buying themselves an arbitrarily long replay window.
        if (sentAt < now - window || sentAt > now + window)
        {
            return Verdict.StaleTimestamp;
        }

        string expected = Compute(secret, timestamp, body);

        // Constant time. An ordinary comparison returns sooner for a wrong first
        // character than a wrong last one, which turns a 256-bit signature into
        // 64 separate one-character problems.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(presentedSignature.Trim()))
            ? Verdict.Valid
            : Verdict.Mismatch;
    }
}

/// <summary>
/// A webhook that has been accepted, kept so the same one is not accepted twice.
/// <para>
/// Replay protection needs memory, and this is it: one row per signature seen,
/// swept once the tolerance window has passed. A correctly signed request that
/// arrives twice is authentic both times — the signature proves who sent it and
/// says nothing about whether it has already been acted on.
/// </para>
/// <para>
/// The rows are short-lived by design. Nothing older than the tolerance window
/// can be replayed anyway, so keeping it would be storing a growing table to
/// answer a question that has already been answered by the clock.
/// </para>
/// </summary>
public sealed class WebhookReceipt : Entity
{
    private WebhookReceipt() { }

    private WebhookReceipt(
        Guid id,
        string providerCode,
        string signature,
        DateTimeOffset receivedAt)
        : base(id)
    {
        ProviderCode = providerCode;
        Signature = signature;
        ReceivedAt = receivedAt;
    }

    public string ProviderCode { get; private set; } = string.Empty;

    /// <summary>
    /// The signature, which is what makes this request distinguishable from the
    /// next one. Not a secret: it is a hash the sender already published in a
    /// header, and it cannot be turned back into the signing key.
    /// </summary>
    public string Signature { get; private set; } = string.Empty;

    public DateTimeOffset ReceivedAt { get; private set; }

    public static WebhookReceipt Record(
        string providerCode, string signature, DateTimeOffset now)
        => new(Uuid7.NewGuid(now), providerCode, signature, now);
}
