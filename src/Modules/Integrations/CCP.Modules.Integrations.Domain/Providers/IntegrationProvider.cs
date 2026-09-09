using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;

namespace CCP.Modules.Integrations.Domain.Providers;

/// <summary>
/// An external service the Platform is allowed to talk to.
/// <para>
/// <b>Registered as data, not as code.</b> A bank, an SMS gateway, a government
/// API: each is a row carrying where it lives, how patient to be with it, what
/// to hide from the log, and which secret to sign with. Adding one is a
/// registration; adding one in code would make every new provider a deployment
/// and would put the Platform team in the path of every other team's work.
/// </para>
/// <para>
/// What is deliberately <b>not</b> here is anything about what the provider
/// means. The layer transports; it does not interpret. A connector that
/// understood invoices would be a business rule in the Platform, which is the
/// one thing this project exists not to have.
/// </para>
/// </summary>
public sealed class IntegrationProvider : AggregateRoot, IAuditableEntity
{
    private readonly List<IntegrationEndpoint> _endpoints = [];

    private IntegrationProvider() { }

    private IntegrationProvider(
        Guid id,
        string code,
        string name,
        Uri baseAddress,
        DateTimeOffset now)
        : base(id)
    {
        Code = code;
        Name = name;
        BaseAddress = baseAddress.ToString();
        IsEnabled = true;
        CreatedAt = now;
    }

    /// <summary>A stable identifier used in routes, logs and configuration.</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Where the provider lives. Every call this provider makes is resolved
    /// against it, and the host must be on the outbound allow-list.
    /// </summary>
    public string BaseAddress { get; private set; } = string.Empty;

    /// <summary>
    /// <b>The name of a secret, never a secret.</b>
    /// <para>
    /// This column holds something like <c>integrations/acme-bank/api-key</c>.
    /// Resolving it to a value happens at call time, through the secret
    /// resolver, and the value never touches this table. A database backup that
    /// leaks is therefore not a credential leak — which is the entire point
    /// (§19.3).
    /// </para>
    /// </summary>
    public string? CredentialReference { get; private set; }

    /// <summary>
    /// Whether calls to this provider are attempted at all. The switch somebody
    /// pulls at three in the morning.
    /// </summary>
    public bool IsEnabled { get; private set; }

    /// <summary>
    /// How long to wait for a response before giving up on one attempt.
    /// <para>
    /// Per provider because patience is a property of the provider, not of the
    /// Platform: a payment gateway that answers in 200 milliseconds and a
    /// government service that takes twenty seconds cannot share a number, and
    /// the shared number would be wrong for both.
    /// </para>
    /// </summary>
    public TimeSpan Timeout { get; private set; } = TimeSpan.FromSeconds(10);

    /// <summary>How many times to try again after a failure worth retrying.</summary>
    public int MaxRetries { get; private set; } = 2;

    /// <summary>
    /// How many consecutive failures open the circuit.
    /// <para>
    /// After that, calls fail immediately without reaching the provider. That is
    /// not giving up — it is refusing to spend the Platform's threads waiting on
    /// something known to be down, which is how one provider's outage becomes
    /// everybody's (§19.6).
    /// </para>
    /// </summary>
    public int FailuresBeforeBreaking { get; private set; } = 5;

    /// <summary>How long the circuit stays open before one attempt is allowed through.</summary>
    public TimeSpan BreakDuration { get; private set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many calls to this provider may be in flight at once.
    /// <para>
    /// The bulkhead. A provider that has become slow but not failed will absorb
    /// every thread the Platform has if nothing stops it, and the symptom is not
    /// "the integration is slow" — it is that the whole Platform stops
    /// answering. This is the wall between the two.
    /// </para>
    /// </summary>
    public int MaxConcurrentCalls { get; private set; } = 8;

    /// <summary>
    /// Field names to blank before anything is written to the call log, one per
    /// line.
    /// <para>
    /// Declared per provider because only the provider's owner knows which of
    /// its fields carry a card number, a national id or a bearer token. A
    /// Platform-wide list would be a guess, and a guess here is a leak that
    /// survives in a log for years.
    /// </para>
    /// </summary>
    public string RedactedFields { get; private set; } = string.Empty;

    public IReadOnlyList<IntegrationEndpoint> Endpoints => _endpoints.AsReadOnly();

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>The declared redaction list, parsed.</summary>
    public IReadOnlyList<string> RedactionPolicy =>
        RedactedFields.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static Result<IntegrationProvider> Create(
        string code,
        string name,
        string baseAddress,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure<IntegrationProvider>(IntegrationErrors.ProviderCodeRequired);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<IntegrationProvider>(IntegrationErrors.ProviderNameRequired);
        }

        if (!Uri.TryCreate(baseAddress, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return Result.Failure<IntegrationProvider>(IntegrationErrors.BaseAddressInvalid);
        }

        return Result.Success(new IntegrationProvider(
            Uuid7.NewGuid(now), code.Trim().ToLowerInvariant(), name.Trim(), uri, now));
    }

    /// <summary>
    /// Sets how patient to be with this provider, and how quickly to stop
    /// trying.
    /// </summary>
    public Result ConfigureResilience(
        TimeSpan timeout,
        int maxRetries,
        int failuresBeforeBreaking,
        TimeSpan breakDuration,
        int maxConcurrentCalls,
        DateTimeOffset now)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(2))
        {
            return Result.Failure(IntegrationErrors.TimeoutOutOfRange);
        }

        if (maxRetries is < 0 or > 5)
        {
            // More than a handful is not resilience, it is a caller refusing to
            // accept an answer — and every retry is load the provider did not
            // ask for while it is already struggling.
            return Result.Failure(IntegrationErrors.RetriesOutOfRange);
        }

        if (failuresBeforeBreaking < 1 || maxConcurrentCalls < 1)
        {
            return Result.Failure(IntegrationErrors.ResilienceValueOutOfRange);
        }

        Timeout = timeout;
        MaxRetries = maxRetries;
        FailuresBeforeBreaking = failuresBeforeBreaking;
        BreakDuration = breakDuration;
        MaxConcurrentCalls = maxConcurrentCalls;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Points the provider at a secret. The value is never supplied here, and
    /// there is no method that accepts one.
    /// </summary>
    public Result SetCredentialReference(string? reference, DateTimeOffset now)
    {
        // A value that looks like a secret rather than a name is almost
        // certainly somebody pasting the secret itself. Refused, because the
        // alternative is a credential in the database and in every backup of it.
        if (reference is not null && LooksLikeASecret(reference))
        {
            return Result.Failure(IntegrationErrors.CredentialValueSupplied);
        }

        CredentialReference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        UpdatedAt = now;

        return Result.Success();
    }

    public Result SetRedactionPolicy(IReadOnlyList<string> fieldNames, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(fieldNames);

        RedactedFields = string.Join(
            '\n',
            fieldNames.Select(f => f.Trim().ToLowerInvariant())
                .Where(f => f.Length > 0)
                .Distinct(StringComparer.Ordinal));

        UpdatedAt = now;

        return Result.Success();
    }

    public Result SetEnabled(bool isEnabled, DateTimeOffset now)
    {
        IsEnabled = isEnabled;
        UpdatedAt = now;

        return Result.Success();
    }

    public Result<IntegrationEndpoint> AddEndpoint(
        string key, string method, string pathTemplate, DateTimeOffset now)
    {
        if (_endpoints.Exists(e => string.Equals(e.Key, key.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<IntegrationEndpoint>(IntegrationErrors.EndpointKeyTaken);
        }

        Result<IntegrationEndpoint> endpoint =
            IntegrationEndpoint.Create(Id, key, method, pathTemplate, now);

        if (endpoint.IsSuccess)
        {
            _endpoints.Add(endpoint.Value);
            UpdatedAt = now;
        }

        return endpoint;
    }

    public IntegrationEndpoint? FindEndpoint(string key) =>
        _endpoints.Find(e => string.Equals(e.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether a string is a name pointing at a secret or a secret itself.
    /// <para>
    /// A heuristic, and it says so. It catches the obvious paste — a long
    /// high-entropy token, or something carrying a recognisable secret prefix —
    /// and it will not catch a short password. It is a guard rail, not a
    /// guarantee, and the guarantee is that nothing in this module ever reads a
    /// credential value out of the database because there is no column holding
    /// one.
    /// </para>
    /// </summary>
    private static bool LooksLikeASecret(string value)
    {
        string trimmed = value.Trim();

        if (trimmed.Length > 80)
        {
            return true;
        }

        string[] prefixes = ["sk-", "ccps_", "AKIA", "Bearer ", "-----BEGIN"];

        return prefixes.Any(p => trimmed.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }
}
