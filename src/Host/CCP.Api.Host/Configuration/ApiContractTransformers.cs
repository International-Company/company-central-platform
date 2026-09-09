using System.Globalization;
using CCP.Kernel.Api.Security;
using CCP.Kernel.Api.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CCP.Api.Host.Configuration;

/// <summary>
/// Writes into the published contract what a caller otherwise has to discover by
/// trying.
/// <para>
/// <b>Phase 11 asks for a developer with no access to the source to integrate
/// using the documentation alone.</b> A contract that lists paths and shapes but
/// not the permission each one demands fails that on the first 403: the caller
/// can see what to send and has no way to learn what to be granted.
/// </para>
/// <para>
/// Derived from the endpoint metadata rather than written by hand, so it cannot
/// drift. A permission renamed in code is renamed in the document by the next
/// build, and a hand-maintained table would be wrong within a month.
/// </para>
/// </summary>
public sealed class SecurityAnnotationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<object> metadata = [.. context.Description.ActionDescriptor.EndpointMetadata];

        var notes = new List<string>();

        string[] permissions =
        [
            .. metadata.OfType<RequirePermissionAttribute>()
                .Select(a => a.Permission)
                .Distinct(StringComparer.Ordinal)
        ];

        if (permissions.Length > 0)
        {
            notes.Add($"**Requires:** `{string.Join("`, `", permissions)}`");
        }

        if (metadata.OfType<AuthenticatedUserOnlyAttribute>().Any())
        {
            notes.Add(
                "**Requires:** an authenticated caller, and no permission. "
                + "This endpoint acts only on the caller's own records.");
        }

        if (metadata.OfType<IAllowAnonymous>().Any())
        {
            notes.Add("**Anonymous.** No token is needed to call this.");
        }

        if (metadata.OfType<RequireStepUpAttribute>().Any())
        {
            notes.Add(
                "**Second factor required.** The caller must have completed a step-up "
                + "challenge recently; otherwise this answers 403 with "
                + "`SECURITY.STEP_UP_REQUIRED`.");
        }

        if (metadata.OfType<DeprecationMetadata>().FirstOrDefault() is { } deprecation)
        {
            operation.Deprecated = true;

            notes.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"**Deprecated** since {deprecation.DeprecatedOn:yyyy-MM-dd}. "
                + $"Stops working on {deprecation.SunsetOn:yyyy-MM-dd}. "
                + $"Use `{deprecation.Replacement}` instead."));
        }

        if (notes.Count > 0)
        {
            operation.Description = string.IsNullOrWhiteSpace(operation.Description)
                ? string.Join("\n\n", notes)
                : operation.Description + "\n\n" + string.Join("\n\n", notes);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// The document's own front matter: what this API is, how to authenticate, and
/// where the error codes are written down.
/// <para>
/// It exists because the generated document otherwise opens with a title and
/// nothing else, and the first question every integrator has — "how do I get a
/// token?" — is answered nowhere in it.
/// </para>
/// </summary>
public sealed class PlatformDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Info.Title = "Company Central Platform API";
        document.Info.Version = "v1";

        document.Info.Description =
            """
            Shared infrastructure every business system in the company depends on:
            identity, organization, authorization, security, audit, workflow,
            notifications and documents.

            ## Authenticating

            **A person** signs in at `POST /api/v1/auth/login` and receives an access
            token and a refresh token.

            **A system** exchanges client credentials at `POST /api/v1/oauth/token`:

            ```
            grant_type=client_credentials
            client_id=ccp_...
            client_secret=ccps_...
            ```

            Add `on_behalf_of=<user id>` to act for a person. The resulting call is
            allowed to do only what the application *and* that person may do.

            Both kinds of token are signed with the same key and can be validated
            locally against `/api/v1/.well-known/jwks.json`.

            ## Permissions

            Every operation below states the permission it requires. Holding it is
            necessary and not always sufficient — a permission is granted at a
            *scope*, and the scope decides which records are in reach.

            ## Failures

            Errors are RFC 9457 problem documents carrying a stable `code`. Match on
            the code, never on the message: messages are translated and reworded, and
            codes are not.

            ## Versioning

            The path carries the version. What may change inside `v1` without notice,
            and what cannot, is set out in `docs/api/versioning.md`. Endpoints on their
            way out answer with `Deprecation` and `Sunset` headers before they stop.
            """;

        return Task.CompletedTask;
    }
}
