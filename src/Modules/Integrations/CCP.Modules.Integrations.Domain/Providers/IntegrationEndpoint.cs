using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;

namespace CCP.Modules.Integrations.Domain.Providers;

/// <summary>
/// One operation a provider offers.
/// <para>
/// A key a caller names, a method, and a path template. The caller says "call
/// <c>acme-bank</c> / <c>transfer</c>" and never constructs a URL — which is
/// what makes the allow-list meaningful. A layer that accepted a URL from its
/// caller would be a way to make the Platform fetch anything, from inside the
/// network, with the Platform's own credentials attached.
/// </para>
/// </summary>
public sealed class IntegrationEndpoint : Entity
{
    private IntegrationEndpoint() { }

    private IntegrationEndpoint(
        Guid id,
        Guid providerId,
        string key,
        string method,
        string pathTemplate,
        DateTimeOffset now)
        : base(id)
    {
        ProviderId = providerId;
        Key = key;
        Method = method;
        PathTemplate = pathTemplate;
        CreatedAt = now;
    }

    public Guid ProviderId { get; private set; }

    /// <summary>What a caller names to reach this operation.</summary>
    public string Key { get; private set; } = string.Empty;

    public string Method { get; private set; } = "POST";

    /// <summary>
    /// The path, relative to the provider's base address, with
    /// <c>{placeholders}</c> the caller fills.
    /// <para>
    /// Relative on purpose and enforced as such: an absolute path here would let
    /// an endpoint definition point at a different host than the one the
    /// provider was allow-listed for.
    /// </para>
    /// </summary>
    public string PathTemplate { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    private static readonly string[] AllowedMethods = ["GET", "POST", "PUT", "PATCH", "DELETE"];

    internal static Result<IntegrationEndpoint> Create(
        Guid providerId, string key, string method, string pathTemplate, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Result.Failure<IntegrationEndpoint>(IntegrationErrors.EndpointKeyRequired);
        }

        string upperMethod = method.Trim().ToUpperInvariant();

        if (!AllowedMethods.Contains(upperMethod, StringComparer.Ordinal))
        {
            return Result.Failure<IntegrationEndpoint>(IntegrationErrors.MethodNotAllowed);
        }

        string path = pathTemplate.Trim();

        if (path.Length == 0)
        {
            return Result.Failure<IntegrationEndpoint>(IntegrationErrors.PathTemplateRequired);
        }

        // Anything absolute, protocol-relative, or containing a traversal is
        // refused here rather than caught later. Each of them is a way for a
        // path to reach a host the provider was not registered for, and "later"
        // means after the request has been built with the credential attached.
        if (Uri.TryCreate(path, UriKind.Absolute, out _)
            || path.StartsWith("//", StringComparison.Ordinal)
            || path.Contains("..", StringComparison.Ordinal))
        {
            return Result.Failure<IntegrationEndpoint>(IntegrationErrors.PathTemplateMustBeRelative);
        }

        return Result.Success(new IntegrationEndpoint(
            Uuid7.NewGuid(now), providerId, key.Trim().ToLowerInvariant(), upperMethod,
            path.TrimStart('/'), now));
    }

    /// <summary>
    /// Fills the template from the caller's arguments.
    /// <para>
    /// Every value is URL-escaped. A caller passing an id containing a slash
    /// would otherwise be writing path segments of its own, which is the same
    /// traversal problem arriving through the front door.
    /// </para>
    /// </summary>
    public Result<string> BuildPath(IReadOnlyDictionary<string, string>? arguments)
    {
        string path = PathTemplate;
        var missing = new List<string>();

        int start;

        while ((start = path.IndexOf('{', StringComparison.Ordinal)) >= 0)
        {
            int end = path.IndexOf('}', start);

            if (end < 0)
            {
                return Result.Failure<string>(IntegrationErrors.PathTemplateMalformed);
            }

            string name = path[(start + 1)..end];

            if (arguments is null || !arguments.TryGetValue(name, out string? value))
            {
                missing.Add(name);

                // Replaced with nothing so the loop advances; the failure below
                // is what the caller sees.
                path = path[..start] + path[(end + 1)..];

                continue;
            }

            path = path[..start] + Uri.EscapeDataString(value) + path[(end + 1)..];
        }

        return missing.Count > 0
            ? Result.Failure<string>(IntegrationErrors.MissingPathArguments(missing))
            : Result.Success(path);
    }
}
