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
/// Nobody can hand out a permission they do not hold themselves.
/// <para>
/// <b>The rule was enforced and never tested.</b> The phase 4 acceptance
/// criterion is one of the most consequential in the Platform — without it,
/// anybody allowed to edit roles can give themselves, through a role they then
/// hold, anything at all — and the phase review found the refusal in
/// <c>SetRolePermissionsHandler</c> with nothing exercising it. A refactor that
/// dropped the check would have passed every test.
/// </para>
/// <para>
/// Through the real handler resolved from the running host, against a caller
/// who genuinely holds exactly one permission.
/// </para>
/// </summary>
public sealed class AntiEscalationTests(PlatformApiFactory factory)
    : IClassFixture<PlatformApiFactory>
{
    private const string Held = "platform.users.view";
    private const string NotHeld = "platform.roles.manage";

    [Fact]
    public async Task GrantingAPermissionTheCallerDoesNotHoldIsRefused()
    {
        RoleDto role = await CreateRoleAsync();
        Guid actingUserId = await ActingUserHoldingAsync(Held);
        Guid notHeld = await PermissionIdAsync(NotHeld);

        using IServiceScope scope = factory.Services.CreateScope();

        Result<RoleDto> result = await scope.ServiceProvider
            .GetRequiredService<SetRolePermissionsHandler>()
            .HandleAsync(new SetRolePermissionsCommand(role.Id, [notHeld], actingUserId, 0));

        Assert.True(result.IsFailure, "A caller granted a permission they do not hold.");
        Assert.Equal("AUTHZ.CANNOT_GRANT_UNHELD_PERMISSION", result.Errors[0].Code);
    }

    /// <summary>
    /// The refusal changes nothing.
    /// <para>
    /// Refusing with the right code while having already written the permission
    /// would be the worst version of this defect: a green response log and an
    /// escalated role.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ARefusedGrantLeavesTheRoleUntouched()
    {
        RoleDto role = await CreateRoleAsync();
        Guid actingUserId = await ActingUserHoldingAsync(Held);
        Guid notHeld = await PermissionIdAsync(NotHeld);

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<SetRolePermissionsHandler>()
                .HandleAsync(new SetRolePermissionsCommand(role.Id, [notHeld], actingUserId, 0));
        }

        await using AuthorizationDbContext context = Authorization();

        Assert.False(await context.RolePermissions.AnyAsync(rp => rp.RoleId == role.Id));
    }

    /// <summary>
    /// The same caller can grant what they do hold.
    /// <para>
    /// Without this, the two tests above would pass against a handler that
    /// refused everything.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GrantingAPermissionTheCallerHoldsSucceeds()
    {
        RoleDto role = await CreateRoleAsync();
        Guid actingUserId = await ActingUserHoldingAsync(Held);
        Guid held = await PermissionIdAsync(Held);

        using IServiceScope scope = factory.Services.CreateScope();

        Result<RoleDto> result = await scope.ServiceProvider
            .GetRequiredService<SetRolePermissionsHandler>()
            .HandleAsync(new SetRolePermissionsCommand(role.Id, [held], actingUserId, 0));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
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
                $"ae{Guid.NewGuid():N}"[..16], "دور اختبار", "Escalation test role", null));

        Assert.True(created.IsSuccess, created.IsFailure ? created.Errors[0].Code : null);

        return created.Value;
    }

    private async Task<Guid> PermissionIdAsync(string name)
    {
        await using AuthorizationDbContext context = Authorization();

        return (await context.Permissions.SingleAsync(p => p.Name == name)).Id;
    }

    /// <summary>A user holding exactly one permission, everywhere.</summary>
    private async Task<Guid> ActingUserHoldingAsync(string permission)
    {
        await using AuthorizationDbContext context = Authorization();

        DateTimeOffset now = DateTimeOffset.UtcNow;

        Permission carried = await context.Permissions.SingleAsync(p => p.Name == permission);

        Role holder = Role.Create(
            $"ah{Guid.NewGuid():N}"[..16], "دور حامل", "Holder role", null, now).Value;

        holder.AddPermission(carried.Id, carried.Name, now);
        holder.ClearDomainEvents();

        context.Roles.Add(holder);

        Guid userId = Guid.CreateVersion7();

        context.Assignments.Add(UserRoleAssignment.Grant(
            userId, holder.Id, new GrantedScope(ScopeType.All, null),
            Guid.CreateVersion7(), now, expiresAt: null).Value);

        await context.SaveChangesAsync();

        return userId;
    }

    private AuthorizationDbContext Authorization()
    {
        // Touched first so the host has migrated and seeded before anything
        // reads the permission catalogue.
        _ = factory.Services;

        return new AuthorizationDbContext(
            new DbContextOptionsBuilder<AuthorizationDbContext>()
                .UseNpgsql(factory.TestConnectionString)
                .Options);
    }
}
