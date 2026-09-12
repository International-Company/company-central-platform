using CCP.Kernel.Domain;
using CCP.Kernel.Results;

namespace CCP.Modules.Integrations.Domain.Webhooks;

/// <summary>
/// A standing request from a business application to be told when something
/// happens.
/// <para>
/// <b>The Platform decides <i>that</i> something happened; the application
/// decides what that means</b> (§16.4). A workflow is approved, a document is
/// destroyed, a role is granted — the Platform records the fact and the business
/// system acts on it, and until now the only way for it to find out was to poll.
/// </para>
/// <para>
/// <b>A subscription is an SSRF primitive if it is not governed</b>, which is
/// why this was deferred out of Phase 11 and into the phase that owns outbound
/// calls. Somebody who can register a URL and have the Platform fetch it has a
/// proxy into the network the Platform runs in. So a subscription's address goes
/// through the same allow-list, the same address checks and the same guarded
/// socket as every other outbound call, and it is checked when the subscription
/// is registered rather than discovered when an event fires.
/// </para>
/// </summary>
public sealed class WebhookSubscription : AggregateRoot
{
    private readonly List<string> _eventTypes = [];

    private WebhookSubscription() { }

    private WebhookSubscription(
        Guid id,
        Guid applicationId,
        string name,
        Uri endpoint,
        IEnumerable<string> eventTypes,
        string secretReference,
        DateTimeOffset now)
        : base(id)
    {
        ApplicationId = applicationId;
        Name = name;
        Endpoint = endpoint.ToString();
        SecretReference = secretReference;
        IsEnabled = true;
        CreatedAt = now;
        UpdatedAt = now;

        _eventTypes.AddRange(eventTypes);
    }

    /// <summary>Which registered application asked to be told.</summary>
    public Guid ApplicationId { get; private set; }

    /// <summary>What it is called, for the screen and the log.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Where the Platform posts.</summary>
    public string Endpoint { get; private set; } = string.Empty;

    /// <summary>
    /// The <b>name</b> of the signing secret, never its value.
    /// <para>
    /// Resolved at delivery time from wherever the deployment keeps secrets, the
    /// same way a provider's credential is. There is no column here that could
    /// hold a secret, so a database backup that leaks is not a leak of every
    /// subscriber's signing key.
    /// </para>
    /// </summary>
    public string SecretReference { get; private set; } = string.Empty;

    /// <summary>
    /// The event types this subscription wants.
    /// <para>
    /// <b>Named explicitly; there is no "everything".</b> A subscription that
    /// received every event would receive events its owner has never heard of,
    /// including ones added years later — and the first they would know is a
    /// parser failing on a shape nobody told them about.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> EventTypes => _eventTypes;

    public bool IsEnabled { get; private set; }

    /// <summary>
    /// How many deliveries have failed in a row.
    /// <para>
    /// Reset by any success. It is consecutive rather than cumulative because
    /// "this endpoint is gone" and "this endpoint had a bad week two years ago"
    /// are different facts and only one of them is actionable.
    /// </para>
    /// </summary>
    public int ConsecutiveFailures { get; private set; }

    /// <summary>
    /// When the Platform stopped trying, and why.
    /// <para>
    /// <b>Suspended, not deleted.</b> An endpoint that has been unreachable for
    /// a week is almost certainly gone, and posting to it for ever costs the
    /// Platform a thread every few minutes to learn nothing. But a subscription
    /// that vanished would take its owner's configuration with it, so this is a
    /// state somebody can see and undo.
    /// </para>
    /// </summary>
    public DateTimeOffset? SuspendedAt { get; private set; }

    public string? SuspendedReason { get; private set; }

    public DateTimeOffset? LastDeliveredAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Whether an event should be delivered here right now.</summary>
    public bool IsLive => IsEnabled && SuspendedAt is null;

    public static Result<WebhookSubscription> Register(
        Guid applicationId,
        string name,
        string endpoint,
        IReadOnlyCollection<string> eventTypes,
        string secretReference,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(eventTypes);

        if (applicationId == Guid.Empty)
        {
            return Result.Failure<WebhookSubscription>(
                IntegrationErrors.SubscriptionApplicationRequired);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<WebhookSubscription>(IntegrationErrors.SubscriptionNameRequired);
        }

        Result<Uri> address = ParseEndpoint(endpoint);

        if (address.IsFailure)
        {
            return Result.Failure<WebhookSubscription>(address.Errors);
        }

        Result<IReadOnlyList<string>> types = NormaliseEventTypes(eventTypes);

        if (types.IsFailure)
        {
            return Result.Failure<WebhookSubscription>(types.Errors);
        }

        if (string.IsNullOrWhiteSpace(secretReference))
        {
            // Refused rather than defaulted to unsigned. A webhook the receiver
            // cannot authenticate is a message anybody on the internet can
            // forge, and "we will add signing later" is how it never gets added.
            return Result.Failure<WebhookSubscription>(
                IntegrationErrors.SubscriptionSecretRequired);
        }

        return Result.Success(new WebhookSubscription(
            Guid.CreateVersion7(), applicationId, name.Trim(), address.Value,
            types.Value, secretReference.Trim(), now));
    }

    public Result Reconfigure(
        string name,
        string endpoint,
        IReadOnlyCollection<string> eventTypes,
        string secretReference,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(eventTypes);

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(IntegrationErrors.SubscriptionNameRequired);
        }

        Result<Uri> address = ParseEndpoint(endpoint);

        if (address.IsFailure)
        {
            return Result.Failure(address.Errors);
        }

        Result<IReadOnlyList<string>> types = NormaliseEventTypes(eventTypes);

        if (types.IsFailure)
        {
            return Result.Failure(types.Errors);
        }

        if (string.IsNullOrWhiteSpace(secretReference))
        {
            return Result.Failure(IntegrationErrors.SubscriptionSecretRequired);
        }

        Name = name.Trim();
        Endpoint = address.Value.ToString();
        SecretReference = secretReference.Trim();
        UpdatedAt = now;

        _eventTypes.Clear();
        _eventTypes.AddRange(types.Value);

        return Result.Success();
    }

    public void SetEnabled(bool isEnabled, DateTimeOffset now)
    {
        IsEnabled = isEnabled;
        UpdatedAt = now;
    }

    /// <summary>
    /// Brings a suspended subscription back, and clears the count that suspended
    /// it.
    /// <para>
    /// Clearing the count matters: resuming with the failures still recorded
    /// would suspend it again on the next failure, which looks to its owner like
    /// resuming did nothing.
    /// </para>
    /// </summary>
    public void Resume(DateTimeOffset now)
    {
        SuspendedAt = null;
        SuspendedReason = null;
        ConsecutiveFailures = 0;
        UpdatedAt = now;
    }

    public void RecordSuccess(DateTimeOffset now)
    {
        ConsecutiveFailures = 0;
        LastDeliveredAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Counts a failure and suspends once there have been too many in a row.
    /// </summary>
    /// <returns>Whether this failure was the one that suspended it.</returns>
    public bool RecordFailure(int limit, string reason, DateTimeOffset now)
    {
        ConsecutiveFailures++;
        UpdatedAt = now;

        if (SuspendedAt is not null || ConsecutiveFailures < limit)
        {
            return false;
        }

        SuspendedAt = now;
        SuspendedReason = reason;

        return true;
    }

    public bool Wants(string eventType)
        => _eventTypes.Contains(eventType, StringComparer.Ordinal);

    /// <summary>
    /// What a subscription's address may look like before anything asks the
    /// network.
    /// <para>
    /// The allow-list and the address checks happen at the outbound guard, where
    /// they belong and where they are the same for every outbound call. These
    /// are the refusals that need no lookup, so a mistyped URL fails while
    /// somebody is looking at the form.
    /// </para>
    /// </summary>
    private static Result<Uri> ParseEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)
            || !Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out Uri? uri))
        {
            return Result.Failure<Uri>(IntegrationErrors.SubscriptionEndpointInvalid);
        }

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            return Result.Failure<Uri>(IntegrationErrors.SubscriptionEndpointInvalid);
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            // https://trusted.example@evil.test/ points at evil.test and reads,
            // to a human skimming a list of subscriptions, as the allowed host.
            return Result.Failure<Uri>(IntegrationErrors.SubscriptionEndpointInvalid);
        }

        return Result.Success(uri);
    }

    private static Result<IReadOnlyList<string>> NormaliseEventTypes(
        IReadOnlyCollection<string> eventTypes)
    {
        string[] types = [.. eventTypes
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

        return types.Length == 0
            ? Result.Failure<IReadOnlyList<string>>(IntegrationErrors.SubscriptionEventsRequired)
            : Result.Success<IReadOnlyList<string>>(types);
    }
}
