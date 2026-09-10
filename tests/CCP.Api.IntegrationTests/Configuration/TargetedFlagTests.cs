using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CCP.Modules.Authorization.Domain.Roles;
using CCP.Modules.Authorization.Domain.Scopes;
using CCP.Modules.Authorization.Infrastructure.Persistence;
using CCP.Modules.Configuration.Domain.Flags;
using CCP.Modules.Configuration.Infrastructure.Persistence;
using CCP.Modules.Identity.Domain.Credentials;
using CCP.Modules.Identity.Domain.Users;
using CCP.Modules.Identity.Infrastructure.Persistence;
using CCP.Modules.Identity.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CCP.Api.IntegrationTests.Configuration;

/// <summary>
/// A feature flag aimed at a role actually reaches somebody holding it.
/// <para>
/// <b>This is the test that proves debt #50 is fixed, and it could not have been
/// written as a unit test.</b> The domain's <c>IsOnFor</c> was always correct and
/// always covered; the endpoint called it with two empty lists. Every layer
/// passed its own tests and the feature did not work — a rollout aimed at one
/// department never arrived, nothing failed anywhere, and the screen that asked
/// could not tell that from a flag which was genuinely off.
/// </para>
/// <para>
/// The assertions come in pairs on purpose. "On for the targeted caller" alone
/// would pass on a build that had gone back to ignoring targeting altogether and
/// answering yes to everybody, which is the far worse defect of the two.
/// </para>
/// </summary>
public sealed class TargetedFlagTests(PlatformApiFactory factory)
    : IClassFixture<PlatformApiFactory>
{
    private const string Password = "correct-horse-battery-staple";

    [Fact]
    public async Task AFlagTargetedAtARoleIsOnForSomebodyHoldingIt()
    {
        string key = AKey();
        Guid roleId = await ARoleAsync();

        await AFlagAsync(key, enabled: true, roleTargets: [roleId]);

        Assert.True(await AskAsync(await SignInAsync(roleId), key));
    }

    /// <summary>
    /// And off for somebody who does not hold it. Without this, a build that
    /// ignored targeting and answered yes to everybody would pass.
    /// </summary>
    [Fact]
    public async Task TheSameFlagIsOffForSomebodyWithoutTheRole()
    {
        string key = AKey();
        Guid roleId = await ARoleAsync();

        await AFlagAsync(key, enabled: true, roleTargets: [roleId]);

        Assert.False(await AskAsync(await SignInAsync(role: null), key));
    }

    /// <summary>
    /// Off is off, however it is targeted. The master switch has to be
    /// trustworthy at eight in the evening.
    /// </summary>
    [Fact]
    public async Task ADisabledFlagIsOffEvenForItsTarget()
    {
        string key = AKey();
        Guid roleId = await ARoleAsync();

        await AFlagAsync(key, enabled: false, roleTargets: [roleId]);

        Assert.False(await AskAsync(await SignInAsync(roleId), key));
    }

    /// <summary>
    /// An untargeted flag that is on reaches everybody, including a caller with
    /// no roles and no place in the company.
    /// </summary>
    [Fact]
    public async Task AnUntargetedFlagReachesACallerWithNothing()
    {
        string key = AKey();

        await AFlagAsync(key, enabled: true, roleTargets: []);

        Assert.True(await AskAsync(await SignInAsync(role: null), key));
    }

    /// <summary>
    /// An undeclared key is off. Defaulting to on would make every misspelling a
    /// silent release.
    /// </summary>
    [Fact]
    public async Task AnUndeclaredFlagIsOff()
        => Assert.False(await AskAsync(await SignInAsync(role: null), AKey()));

    // --- Fixtures -----------------------------------------------------------

    /// <summary>
    /// The application that owns these flags. A key must sit inside its owner's
    /// namespace, which is what stops one system declaring a flag into another's
    /// space.
    /// </summary>
    private const string ApplicationCode = "flagtests";

    /// <summary>
    /// Version 4. The leading hex of a UUIDv7 is a millisecond timestamp, so a
    /// truncated one collides between tests running in the same instant.
    /// </summary>
    private static string AKey() => $"{ApplicationCode}.{Guid.NewGuid():N}"[..24];

    private async Task<bool> AskAsync(string token, string key)
    {
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await client.GetAsync(
            new Uri($"/api/v1/me/features/{Uri.EscapeDataString(key)}", UriKind.Relative));

        Assert.True(response.IsSuccessStatusCode, $"The endpoint answered {response.StatusCode}.");

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return body.GetProperty("isOn").GetBoolean();
    }

    private ConfigurationDbContext Configuration() =>
        new(new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseNpgsql(factory.TestConnectionString)
            .Options);

    private AuthorizationDbContext Authorization() =>
        new(new DbContextOptionsBuilder<AuthorizationDbContext>()
            .UseNpgsql(factory.TestConnectionString)
            .Options);

    private async Task AFlagAsync(string key, bool enabled, IReadOnlyList<Guid> roleTargets)
    {
        await using ConfigurationDbContext context = Configuration();

        DateTimeOffset now = DateTimeOffset.UtcNow;

        FeatureFlag flag = FeatureFlag
            .Declare(key, ApplicationCode, "Declared by the targeting suite.", now)
            .Value;

        flag.SetEnabled(enabled, now);
        flag.Target(roleTargets, [], now);

        context.Flags.Add(flag);
        await context.SaveChangesAsync();

        await BumpConfigurationVersionAsync();
    }

    /// <summary>
    /// The configuration snapshot is version-stamped, so a flag written outside
    /// the API must raise the stamp or every read answers from a snapshot taken
    /// before it existed.
    /// </summary>
    private async Task BumpConfigurationVersionAsync()
    {
        await using ConfigurationDbContext context = Configuration();

        await context.Database.ExecuteSqlRawAsync(
            "UPDATE configuration.version SET version = version + 1");
    }

    private async Task<Guid> ARoleAsync()
    {
        await using AuthorizationDbContext context = Authorization();

        DateTimeOffset now = DateTimeOffset.UtcNow;

        Role role = Role.Create(
            $"flag{Guid.NewGuid():N}"[..16], "دور استهداف", "Targeting role",
            "Holds no permission; it exists only to be aimed at.", now).Value;

        context.Roles.Add(role);
        await context.SaveChangesAsync();

        return role.Id;
    }

    /// <summary>
    /// Creates an account, optionally grants it one role, and signs it in.
    /// <para>
    /// The role carries no permissions at all. A flag is aimed at a role, not at
    /// what the role can do, and using a permission-bearing role here would
    /// leave it unclear which of the two the endpoint had actually consulted.
    /// </para>
    /// </summary>
    private async Task<string> SignInAsync(Guid? role)
    {
        string username = $"flg{Guid.NewGuid():N}"[..20];

        await using var identity = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(factory.TestConnectionString)
                .Options);

        User user = User.Create(
            username, $"{username}@example.invalid", "Flag Subject",
            DateTimeOffset.UtcNow, mustChangePassword: false).Value;

        var hasher = new Argon2PasswordHasher(Options.Create(new Argon2Options
        {
            MemoryKib = 8192,
            Iterations = 1,
            Parallelism = 1
        }));

        identity.Users.Add(user);
        identity.Credentials.Add(UserCredential.Create(
            user.Id, hasher.Hash(Password), hasher.AlgorithmId, DateTimeOffset.UtcNow));

        await identity.SaveChangesAsync();

        if (role is { } roleId)
        {
            await using AuthorizationDbContext authorization = Authorization();

            authorization.Assignments.Add(UserRoleAssignment.Grant(
                user.Id, roleId, new GrantedScope(ScopeType.All, null),
                Guid.CreateVersion7(), DateTimeOffset.UtcNow).Value);

            await authorization.SaveChangesAsync();

            PermissionVersionRow version = await authorization.PermissionVersion.FirstAsync();
            version.Version++;
            await authorization.SaveChangesAsync();
        }

        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage login = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { username, password = Password });

        Assert.True(login.IsSuccessStatusCode, $"Sign-in failed: {login.StatusCode}.");

        JsonElement body = await login.Content.ReadFromJsonAsync<JsonElement>();

        return body.GetProperty("accessToken").GetString()!;
    }
}
