using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Authorization.Application.Abstractions;
using CCP.Modules.Authorization.Domain;
using CCP.Modules.Authorization.Domain.Applications;
using CCP.Modules.Authorization.Domain.Permissions;
using CCP.Modules.Authorization.Domain.Roles.Events;

namespace CCP.Modules.Authorization.Application.Applications;

/// <summary>One permission an application declares.</summary>
/// <param name="Name">The full name, which must sit in the application's namespace.</param>
/// <param name="Description">What it allows, for the administration portal.</param>
public sealed record PermissionDeclaration(string Name, string? Description);

/// <summary>An application's complete permission manifest.</summary>
public sealed record DeclarePermissionsCommand(
    string ApplicationCode,
    IReadOnlyList<PermissionDeclaration> Permissions);

/// <summary>
/// Records an application's permission manifest.
/// <para>
/// <b>This is the mechanism that lets the Platform serve systems that do not
/// exist yet</b> (ADR-012). A Financial system declares
/// <c>finance.invoices.approve</c>; the Platform stores it, lets administrators
/// put it in roles, and answers when asked — without ever knowing what an
/// invoice is.
/// </para>
/// <para>
/// The manifest is <b>declarative and idempotent</b>: an application sends its
/// whole list on every startup, and this reconciles. That matters because the
/// alternative — incremental add and remove calls — drifts the moment one call
/// fails, and nobody notices until a permission check fails in production.
/// </para>
/// <para>
/// Permissions no longer declared are <b>deactivated, not deleted</b>. Role
/// assignments still reference them, and audit records from last year still name
/// them.
/// </para>
/// </summary>
public sealed class DeclarePermissionsHandler(
    IAuthorizationRepository repository,
    IPermissionVersionStore versionStore,
    IAuthorizationOutbox outbox,
    IAuthorizationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<PermissionDeclarationResult>> HandleAsync(
        DeclarePermissionsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;

        RegisteredApplication? application =
            await repository.FindApplicationByCodeAsync(command.ApplicationCode, cancellationToken);

        if (application is null)
        {
            return Result.Failure<PermissionDeclarationResult>(AuthorizationErrors.ApplicationNotFound);
        }

        IReadOnlyList<Permission> existing =
            await repository.GetPermissionsForApplicationAsync(application.Id, cancellationToken);

        var existingByName = existing.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var declaredNames = new HashSet<string>(StringComparer.Ordinal);

        int added = 0;

        foreach (PermissionDeclaration declaration in command.Permissions)
        {
            Result<Permission> parsed = Permission.Declare(
                application.Id, application.Code, declaration.Name, declaration.Description, now);

            // A name outside the application's own namespace is rejected here.
            // Without that, a registered business system could declare
            // platform.users.create and grant itself the Platform's own rights.
            if (parsed.IsFailure)
            {
                return Result.Failure<PermissionDeclarationResult>(parsed.Errors);
            }

            declaredNames.Add(parsed.Value.Name);

            if (existingByName.TryGetValue(parsed.Value.Name, out Permission? current))
            {
                // Already known. Refresh its description and make sure it is
                // active again if it had previously disappeared.
                current.UpdateDescription(declaration.Description ?? current.Description, now);
                current.Reactivate(now);
            }
            else
            {
                repository.AddPermission(parsed.Value);
                added++;
            }
        }

        int deactivated = 0;

        foreach (Permission permission in existing)
        {
            if (!declaredNames.Contains(permission.Name) && permission.IsActive)
            {
                permission.Deactivate(now);
                deactivated++;
            }
        }

        await outbox.EnqueueAsync(
            new PermissionsDeclaredEvent(
                application.Id, application.Code, added, deactivated, now),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Deactivating a permission changes what its holders may do, so the
        // stamp must move even when nothing was added.
        if (added > 0 || deactivated > 0)
        {
            await versionStore.BumpAsync(cancellationToken);
        }

        return Result.Success(new PermissionDeclarationResult(
            added, deactivated, command.Permissions.Count));
    }
}

/// <summary>What a declaration changed.</summary>
/// <param name="Added">Permissions that did not previously exist.</param>
/// <param name="Deactivated">Permissions no longer declared.</param>
/// <param name="TotalDeclared">The size of the manifest.</param>
public sealed record PermissionDeclarationResult(int Added, int Deactivated, int TotalDeclared);
