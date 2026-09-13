using CCP.Kernel.Infrastructure.Persistence;
using CCP.Kernel.Results;
using CCP.Modules.Authorization.Application.Roles;
using CCP.Modules.Authorization.Contracts.Dtos;
using CCP.Modules.Authorization.Domain.Permissions;
using CCP.Modules.Authorization.Domain.Roles;
using CCP.Modules.Authorization.Domain.Scopes;
using CCP.Modules.Authorization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CCP.Api.IntegrationTests.Authorization;

/// <summary>
/// Changing a role tells the systems subscribed to it.
/// <para>
/// <b>It did not.</b> <c>authz.role.created</c> was declared and never
/// constructed, and a role gaining or losing a permission raised an event on the
/// aggregate that nothing ever staged — <c>Entity</c> said the unit of work
/// collected raised events, and none did. A business system subscribed by
/// webhook to the widest single change to access the Platform allows would
/// have waited for deliveries that could not come.
/// </para>
/// <para>
/// Through the real handlers resolved from the running host, so the transaction
/// that must carry the event is the production one.
/// </para>
/// </summary>
public sealed class RoleEventPublicationTests(PlatformApiFactory factory)
    : IClassFixture<PlatformApiFactory>
{
    private const string CarriedPermission = "platform.users.view";

    [Fact]
    public async Task CreatingARoleStagesItsEvent()
    {
        RoleDto role = await CreateRoleAsync();

        Assert.True(
            await StagedAsync("authz.role.created", role.Code),
            "Creating a role must stage authz.role.created in the same transaction.");
    }

    [Fact]
    public async Task GrantingAPermissionToARoleStagesItsEvent()
    {
        RoleDto role = await CreateRoleAsync();
        (Guid actingUserId, Guid permissionId) = await ActingUserHoldingAsync(CarriedPermission);

        using IServiceScope scope = factory.Services.CreateScope();

        Result<RoleDto> set = await scope.ServiceProvider
            .GetRequiredService<SetRolePermissionsHandler>()
            .HandleAsync(new SetRolePermissionsCommand(role.Id, [permissionId], actingUserId, 0));

        Assert.True(set.IsSuccess, set.IsFailure ? set.Errors[0].Code : null);

        Assert.True(
            await StagedAsync("authz.role.permission_granted", role.Id.ToString()),
            "A role gaining a permission must stage authz.role.permission_granted.");
    }

    [Fact]
    public async Task RemovingAPermissionFromARoleStagesItsEvent()
    {
        RoleDto role = await CreateRoleAsync();
        (Guid actingUserId, Guid permissionId) = await ActingUserHoldingAsync(CarriedPermission);

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            Result<RoleDto> granted = await scope.ServiceProvider
                .GetRequiredService<SetRolePermissionsHandler>()
                .HandleAsync(new SetRolePermissionsCommand(role.Id, [permissionId], actingUserId, 0));

            Assert.True(granted.IsSuccess, granted.IsFailure ? granted.Errors[0].Code : null);
        }

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            Result<RoleDto> emptied = await scope.ServiceProvider
                .GetRequiredService<SetRolePermissionsHandler>()
                .HandleAsync(new SetRolePermissionsCommand(role.Id, [], actingUserId, 0));

            Assert.True(emptied.IsSuccess, emptied.IsFailure ? emptied.Errors[0].Code : null);
        }

        Assert.True(
            await StagedAsync("authz.role.permission_revoked", role.Id.ToString()),
            "A role losing a permission must stage authz.role.permission_revoked.");
    }

    // --- Fixtures -----------------------------------------------------------

    private async Task<RoleDto> CreateRoleAsync()
    {
        using IServiceScope scope = factory.Services.CreateScope();

        // Version 4, not 7: a truncated UUIDv7 is mostly timestamp and collides
        // between tests running in the same instant.
        Result<RoleDto> created = await scope.ServiceProvider
            .GetRequiredService<CreateRoleHandler>()
            .HandleAsync(new CreateRoleCommand(
                $"ev{Guid.NewGuid():N}"[..16], "دور اختبار", "Event test role", null));

        Assert.True(created.IsSuccess, created.IsFailure ? created.Errors[0].Code : null);

        return created.Value;
    }

    /// <summary>
    /// A user holding one permission everywhere, written straight to the tables.
    /// <para>
    /// The handler refuses to grant a permission the caller does not hold, which
    /// is the anti-escalation rule and not something this suite should route
    /// around — so the caller genuinely holds it.
    /// </para>
    /// </summary>
    private async Task<(Guid UserId, Guid PermissionId)> ActingUserHoldingAsync(string permission)
    {
        _ = factory.Services;

        await using var context = new AuthorizationDbContext(
            new DbContextOptionsBuilder<AuthorizationDbContext>()
                .UseNpgsql(factory.TestConnectionString)
                .Options);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        Permission carried = await context.Permissions.SingleAsync(p => p.Name == permission);

        Role holder = Role.Create(
            $"eh{Guid.NewGuid():N}"[..16], "دور حامل", "Holder role", null, now).Value;

        holder.AddPermission(carried.Id, carried.Name, now);
        holder.ClearDomainEvents();

        context.Roles.Add(holder);

        Guid userId = Guid.CreateVersion7();

        context.Assignments.Add(UserRoleAssignment.Grant(
            userId, holder.Id, new GrantedScope(ScopeType.All, null),
            Guid.CreateVersion7(), now, expiresAt: null).Value);

        await context.SaveChangesAsync();

        return (userId, carried.Id);
    }

    private async Task<bool> StagedAsync(string eventType, string payloadMarker)
    {
        await using KernelDbContext kernel = factory.CreateDbContext();

        // The payload column is jsonb, which has no LIKE: filter on the type in
        // SQL and look inside the payload here.
        List<string> payloads = await kernel.OutboxMessages
            .Where(message => message.EventType == eventType)
            .Select(message => message.Payload)
            .ToListAsync();

        return payloads.Any(payload => payload.Contains(payloadMarker, StringComparison.Ordinal));
    }
}
