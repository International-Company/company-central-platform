namespace CCP.Modules.Integrations.Application;

/// <summary>
/// What the company has decided about talking to the outside world.
/// <para>
/// No credential appears here, and none ever will: this holds policy, and policy
/// belongs in configuration a reviewer can read. Secrets are named by the
/// providers and resolved through the secret resolver (§19.3).
/// </para>
/// </summary>
public sealed class IntegrationOptions
{
    public const string SectionName = "Integrations";

    /// <summary>
    /// The only hosts the Platform may call.
    /// <para>
    /// <b>Empty means no outbound calls at all</b>, and that is the correct
    /// default. A deny-by-default list that starts empty fails visibly on the
    /// first call and is fixed in a minute; an allow-by-default one fails
    /// invisibly, and the failure is that the Platform will fetch any URL
    /// somebody can get it to fetch (§19.1).
    /// </para>
    /// <para>
    /// An entry is matched exactly, or as a subdomain when it begins with a dot:
    /// <c>.example.com</c> covers <c>api.example.com</c> and does not cover
    /// <c>notexample.com</c>.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> AllowedHosts { get; set; } = [];

    /// <summary>
    /// How long call log rows are kept. Ninety days by default.
    /// <para>
    /// Long enough to settle a dispute, short enough that the table does not
    /// become the largest in the database. It grows with traffic rather than
    /// with the company, which is why it has a retention policy from the first
    /// day rather than from the day somebody notices (§19.4).
    /// </para>
    /// </summary>
    public TimeSpan CallLogRetention { get; set; } = TimeSpan.FromDays(90);

    /// <summary>How often the retention sweep runs.</summary>
    public TimeSpan RetentionSweepInterval { get; set; } = TimeSpan.FromHours(12);

    /// <summary>How many rows one sweep pass removes.</summary>
    public int RetentionBatchSize { get; set; } = 5_000;

    /// <summary>
    /// The largest inbound webhook body accepted. 256 KB.
    /// <para>
    /// The body is buffered whole in order to verify its signature — the
    /// signature is over the raw bytes, so there is no way to check it while
    /// streaming. A bound is therefore not optional: without one, an
    /// unauthenticated caller decides how much memory the Platform allocates.
    /// </para>
    /// </summary>
    public int MaxWebhookBodyBytes { get; set; } = 256 * 1024;

    /// <summary>
    /// How far an inbound webhook's timestamp may be from now. Five minutes.
    /// <para>
    /// This is the replay window, and receipts are kept for twice it — long
    /// enough that nothing inside the window can slip past the memory, short
    /// enough that the table stays small.
    /// </para>
    /// </summary>
    public TimeSpan WebhookTolerance { get; set; } = TimeSpan.FromMinutes(5);

    // --- Outbound webhooks --------------------------------------------------

    /// <summary>How often queued webhook deliveries are attempted.</summary>
    public TimeSpan WebhookPollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>How many deliveries one pass claims.</summary>
    public int WebhookBatchSize { get; set; } = 50;

    /// <summary>
    /// How long one attempt may take.
    /// <para>
    /// Short. A subscriber's endpoint is somebody else's server, and a sweep
    /// that waits two minutes for each of fifty deliveries is a sweep that runs
    /// once an hour.
    /// </para>
    /// </summary>
    public TimeSpan WebhookTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How many times a delivery is attempted before it is abandoned.
    /// <para>
    /// Six, which with the backoff below spans roughly an hour — long enough to
    /// ride out a deployment on the subscriber's side, short enough that a
    /// genuinely dead endpoint is visible the same morning.
    /// </para>
    /// </summary>
    public int WebhookMaximumAttempts { get; set; } = 6;

    /// <summary>The first wait after a failure. Each retry roughly doubles it.</summary>
    public TimeSpan WebhookInitialBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many consecutive failures suspend a subscription.
    /// <para>
    /// Counted on the subscription rather than on a delivery, so a hundred
    /// events to a dead endpoint suspend it once. Suspended, not deleted: the
    /// owner's configuration survives and somebody can resume it.
    /// </para>
    /// </summary>
    public int WebhookFailuresBeforeSuspending { get; set; } = 20;
}
