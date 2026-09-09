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

    public static Error CredentialUnresolvable(string reference) => Error.Unexpected(
        "INTEGRATIONS.CREDENTIAL_UNRESOLVABLE",
        $"The secret '{reference}' could not be resolved. The provider is configured and its "
        + "credential is not available.");

    // --- Endpoints ----------------------------------------------------------

    public static readonly Error EndpointKeyRequired = Error.Validation(
        "INTEGRATIONS.ENDPOINT_KEY_REQUIRED", "An endpoint needs a key.", "key");

    public static readonly Error EndpointKeyTaken = Error.Conflict(
        "INTEGRATIONS.ENDPOINT_KEY_TAKEN", "This provider already has an endpoint with that key.");

    public static readonly Error EndpointNotFound = Error.NotFound(
        "INTEGRATIONS.ENDPOINT_NOT_FOUND", "The provider has no endpoint with that key.");

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

    /// <summary>
    /// The address was refused. One error for every reason, deliberately.
    /// <para>
    /// A caller learning <i>why</i> a host was refused learns the shape of the
    /// internal network — which addresses are private, which names resolve
    /// inside — one probe at a time. The Platform's own log records exactly
    /// which check failed.
    /// </para>
    /// </summary>
    public static readonly Error DestinationNotAllowed = Error.Forbidden(
        "INTEGRATIONS.DESTINATION_NOT_ALLOWED",
        "The Platform is not permitted to call that address.");

    public static readonly Error CircuitOpen = Error.Conflict(
        "INTEGRATIONS.CIRCUIT_OPEN",
        "This provider is failing and calls to it are suspended. It will be tried again shortly.");

    public static readonly Error CallTimedOut = Error.Unexpected(
        "INTEGRATIONS.CALL_TIMED_OUT", "The provider did not answer in time.");

    public static Error CallFailed(string reason) => Error.Unexpected(
        "INTEGRATIONS.CALL_FAILED", $"The call could not be completed: {reason}");

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
