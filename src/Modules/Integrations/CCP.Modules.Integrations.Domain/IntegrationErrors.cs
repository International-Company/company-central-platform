using CCP.Kernel.Results;

namespace CCP.Modules.Integrations.Domain;

/// <summary>
/// Every way an outbound call or an inbound webhook can be refused.
/// </summary>
public static class IntegrationErrors
{
    // --- Providers ----------------------------------------------------------

    public static readonly Error ProviderCodeRequired = Error.Validation(
        "INTEGRATIONS.PROVIDER_CODE_REQUIRED", "A provider needs a code.", "code");

    public static readonly Error ProviderNameRequired = Error.Validation(
        "INTEGRATIONS.PROVIDER_NAME_REQUIRED", "A provider needs a name.", "name");

    public static readonly Error ProviderCodeTaken = Error.Conflict(
        "INTEGRATIONS.PROVIDER_CODE_TAKEN", "A provider with this code already exists.");

    public static readonly Error ProviderNotFound = Error.NotFound(
        "INTEGRATIONS.PROVIDER_NOT_FOUND", "The provider is not registered.");

    public static readonly Error ProviderDisabled = Error.Conflict(
        "INTEGRATIONS.PROVIDER_DISABLED", "Calls to this provider are switched off.");

    public static readonly Error BaseAddressInvalid = Error.Validation(
        "INTEGRATIONS.BASE_ADDRESS_INVALID",
        "The base address must be an absolute http or https URL.",
        "baseAddress");

    public static readonly Error TimeoutOutOfRange = Error.Validation(
        "INTEGRATIONS.TIMEOUT_OUT_OF_RANGE",
        "A timeout must be between zero and two minutes.",
        "timeout");

    public static readonly Error RetriesOutOfRange = Error.Validation(
        "INTEGRATIONS.RETRIES_OUT_OF_RANGE",
        "Between zero and five retries. More than that is not resilience — it is load a "
        + "struggling provider did not ask for.",
        "maxRetries");

    public static readonly Error ResilienceValueOutOfRange = Error.Validation(
        "INTEGRATIONS.RESILIENCE_VALUE_OUT_OF_RANGE",
        "The failure threshold and the concurrency limit must both be at least one.",
        "resilience");

    /// <summary>
    /// Somebody pasted a secret where a name belongs.
    /// <para>
    /// Refused loudly, because accepting it would put a live credential into the
    /// database and into every backup of it — which is precisely what
    /// credential-by-reference exists to prevent (§19.3).
    /// </para>
    /// </summary>
    public static readonly Error CredentialValueSupplied = Error.Validation(
        "INTEGRATIONS.CREDENTIAL_VALUE_SUPPLIED",
        "That looks like a secret rather than the name of one. This field holds a reference — "
        + "such as 'integrations/acme/api-key' — and the value lives in the secret store.",
        "credentialReference");

    // --- Webhook subscriptions ----------------------------------------------

    public static readonly Error SubscriptionApplicationRequired = Error.Validation(
        "INTEGRATIONS.SUBSCRIPTION_APPLICATION_REQUIRED",
        "A subscription belongs to a registered application.", "applicationId");

    public static readonly Error SubscriptionNameRequired = Error.Validation(
        "INTEGRATIONS.SUBSCRIPTION_NAME_REQUIRED", "A subscription needs a name.", "name");

    /// <summary>
    /// The address is not one the Platform will post to.
    /// <para>
    /// Refusals that need no network lookup: a relative address, a scheme that
    /// is not http or https, or credentials in the URL — the last because
    /// <c>https://trusted.example@evil.test/</c> points at evil.test and reads,
    /// to a human skimming a list of subscriptions, as the allowed host.
    /// </para>
    /// <para>
    /// Whether the host is <i>allowed</i> is the outbound guard's answer, not
    /// this one, and it is the same answer for every outbound call.
    /// </para>
    /// </summary>
    public static readonly Error SubscriptionEndpointInvalid = Error.Validation(
        "INTEGRATIONS.SUBSCRIPTION_ENDPOINT_INVALID",
        "That is not an address the Platform will post to. Use an absolute http or https URL "
        + "with no credentials in it.",
        "endpoint");

    public static readonly Error SubscriptionEventsRequired = Error.Validation(
        "INTEGRATIONS.SUBSCRIPTION_EVENTS_REQUIRED",
        "Name at least one event type. There is deliberately no way to ask for everything: a "
        + "subscription that received every event would receive ones nobody told you about.",
        "eventTypes");

    /// <summary>
    /// No signing secret was named.
    /// <para>
    /// Refused rather than defaulted to unsigned. A webhook the receiver cannot
    /// authenticate is a message anybody on the internet can forge, and "we will
    /// add signing later" is how it never gets added.
    /// </para>
    /// </summary>
    public static readonly Error SubscriptionSecretRequired = Error.Validation(
        "INTEGRATIONS.SUBSCRIPTION_SECRET_REQUIRED",
        "Name the secret the Platform should sign with. This is a reference such as "
        + "'integrations/acme/webhook-secret'; the value lives in the secret store.",
        "secretReference");

    public static readonly Error SubscriptionNotFound = Error.NotFound(
        "INTEGRATIONS.SUBSCRIPTION_NOT_FOUND", "The subscription does not exist.");

    /// <summary>
    /// The address is refused by the outbound policy.
    /// <para>
    /// Checked when the subscription is registered rather than discovered when
    /// an event fires. A subscription nobody can deliver to is a subscription
    /// whose owner believes they are being told things.
    /// </para>
    /// </summary>
    public static readonly Error SubscriptionEndpointRefused = Error.Validation(
        "INTEGRATIONS.SUBSCRIPTION_ENDPOINT_REFUSED",
        "The Platform is not allowed to call that address. Add the host to the outbound "
        + "allow-list first.",
        "endpoint");

    // --- Endpoints ----------------------------------------------------------

    public static readonly Error EndpointKeyRequired = Error.Validation(
        "INTEGRATIONS.ENDPOINT_KEY_REQUIRED", "An endpoint needs a key.", "key");

    public static readonly Error EndpointKeyTaken = Error.Conflict(
        "INTEGRATIONS.ENDPOINT_KEY_TAKEN", "This provider already has an endpoint with that key.");

    public static readonly Error MethodNotAllowed = Error.Validation(
        "INTEGRATIONS.METHOD_NOT_ALLOWED", "Use GET, POST, PUT, PATCH or DELETE.", "method");

    public static readonly Error PathTemplateRequired = Error.Validation(
        "INTEGRATIONS.PATH_TEMPLATE_REQUIRED", "An endpoint needs a path.", "pathTemplate");

    public static readonly Error PathTemplateMustBeRelative = Error.Validation(
        "INTEGRATIONS.PATH_TEMPLATE_MUST_BE_RELATIVE",
        "The path is relative to the provider's base address. An absolute URL here would let an "
        + "endpoint point somewhere the provider was never allowed to reach.",
        "pathTemplate");

    public static readonly Error PathTemplateMalformed = Error.Validation(
        "INTEGRATIONS.PATH_TEMPLATE_MALFORMED",
        "The path has an unclosed placeholder.",
        "pathTemplate");

    public static Error MissingPathArguments(IReadOnlyList<string> names) => Error.Validation(
        "INTEGRATIONS.MISSING_PATH_ARGUMENTS",
        $"The path needs {string.Join(", ", names)}, and they were not supplied.",
        "arguments");

    // --- Outbound policy ----------------------------------------------------

    public static readonly Error CircuitOpen = Error.Conflict(
        "INTEGRATIONS.CIRCUIT_OPEN",
        "This provider is failing and calls to it are suspended. It will be tried again shortly.");

    // --- Inbound webhooks ---------------------------------------------------

    /// <summary>
    /// A webhook was refused. Also one error for every reason.
    /// <para>
    /// Telling a sender that the signature was wrong but the timestamp was fine
    /// is telling an attacker which half to work on next.
    /// </para>
    /// </summary>
    public static readonly Error WebhookRejected = Error.Unauthenticated(
        "INTEGRATIONS.WEBHOOK_REJECTED", "The webhook could not be verified.");

    public static readonly Error WebhookNotConfigured = Error.NotFound(
        "INTEGRATIONS.WEBHOOK_NOT_CONFIGURED",
        "This provider does not accept webhooks, or has no signing secret configured.");

    public static readonly Error WebhookTooLarge = Error.Validation(
        "INTEGRATIONS.WEBHOOK_TOO_LARGE",
        "The webhook body is larger than this Platform accepts.",
        "body");
}
