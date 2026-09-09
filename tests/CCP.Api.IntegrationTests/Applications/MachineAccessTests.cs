using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CCP.Modules.Authorization.Domain.Applications;
using CCP.Modules.Authorization.Domain.Roles;
using CCP.Modules.Authorization.Domain.Scopes;
using CCP.Modules.Authorization.Infrastructure.Persistence;
using CCP.Modules.Authorization.Infrastructure.Security;
using CCP.Modules.Identity.Domain.Credentials;
using CCP.Modules.Identity.Domain.Users;
using CCP.Modules.Identity.Infrastructure.Persistence;
using CCP.Modules.Identity.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CCP.Api.IntegrationTests.Applications;

/// <summary>
/// A system authenticating as itself, against a real database.
/// <para>
/// <b>Phase 11's acceptance criteria in executable form:</b> client credentials
/// work, rotation causes no downtime, revocation is immediate, and an
/// application cannot exceed what it was granted.
/// </para>
/// <para>
/// The application and its credentials are created directly in the database
/// rather than through the API, because registering one demands a second factor
/// and that is a different test. What is exercised here is the part that runs a
/// thousand times a day: the exchange, and what the resulting token can do.
/// </para>
/// </summary>
public sealed class MachineAccessTests(PlatformApiFactory factory)
    : IClassFixture<PlatformApiFactory>
{
    private const string ValidPassword = "correct-horse-battery-staple";

    [Fact]
    public async Task ClientCredentialsAreExchangedForAToken()
    {
        using HttpClient client = factory.CreateClient();

        (_, string secret, string clientId) =
            await RegisterApplicationAsync("platform-administrator");

        using HttpResponseMessage response = await RequestTokenAsync(client, clientId, secret);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // The shape RFC 6749 specifies, snake case and all, because every OAuth
        // client library already parses exactly this.
        Assert.Equal("Bearer", body.GetProperty("token_type").GetString());
        Assert.True(body.GetProperty("expires_in").GetInt32() > 0);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("access_token").GetString()));
    }

    [Fact]
    public async Task TheTokenCarriesTheApplicationsOwnPermissions()
    {
        using HttpClient client = factory.CreateClient();

        (_, string secret, string clientId) =
            await RegisterApplicationAsync("platform-administrator");

        string token = await GetTokenAsync(client, clientId, secret);

        using HttpResponseMessage response = await SendAsync(
            client, token, HttpMethod.Get, "/api/v1/users?page=1&pageSize=1");

        // Granted the administrator role, so it reads users — through the same
        // resolver, the same roles and the same scopes a person would.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnApplicationWithNoRolesCanAuthenticateAndDoNothing()
    {
        using HttpClient client = factory.CreateClient();

        (_, string secret, string clientId) = await RegisterApplicationAsync(roleCode: null);

        string token = await GetTokenAsync(client, clientId, secret);

        using HttpResponseMessage response = await SendAsync(
            client, token, HttpMethod.Get, "/api/v1/users?page=1&pageSize=1");

        // Authentication and authorization are different questions, and the
        // answer to the first is not the answer to the second.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AMachineTokenIsNotTreatedAsAPerson()
    {
        using HttpClient client = factory.CreateClient();

        (_, string secret, string clientId) =
            await RegisterApplicationAsync("platform-administrator");

        string token = await GetTokenAsync(client, clientId, secret);

        // Its subject claim holds an application id, which is a Guid and would
        // parse perfectly. An endpoint written for people must refuse it rather
        // than quietly operate on a person's records under that id.
        using HttpResponseMessage response = await SendAsync(
            client, token, HttpMethod.Get, "/api/v1/me/permissions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AWrongSecretIsRefusedWithNoHintAboutWhy()
    {
        using HttpClient client = factory.CreateClient();

        (_, _, string clientId) = await RegisterApplicationAsync("platform-administrator");

        using HttpResponseMessage wrongSecret =
            await RequestTokenAsync(client, clientId, "ccps_not-the-right-one");

        using HttpResponseMessage unknownClient =
            await RequestTokenAsync(client, "ccp_never-existed", "ccps_anything");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongSecret.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownClient.StatusCode);

        // The same code for both. Anything more helpful tells an attacker with a
        // list of guessed client ids which ones are real.
        JsonElement first = await wrongSecret.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement second = await unknownClient.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("AUTHZ.INVALID_CLIENT", first.GetProperty("code").GetString());
        Assert.Equal("AUTHZ.INVALID_CLIENT", second.GetProperty("code").GetString());
    }

    [Fact]
    public async Task RotationWorksWithoutDowntimeAndRevocationIsImmediate()
    {
        using HttpClient client = factory.CreateClient();

        (Guid applicationId, string oldSecret, string oldClientId) =
            await RegisterApplicationAsync("platform-administrator");

        // The new key is issued while the old one is still live. That overlap is
        // the whole of "rotation without downtime": the owning team deploys the
        // new secret at its own pace and revokes the old one afterwards.
        (string newSecret, string newClientId) = await IssueSecondCredentialAsync(applicationId);

        using (HttpResponseMessage withOld = await RequestTokenAsync(client, oldClientId, oldSecret))
        {
            Assert.Equal(HttpStatusCode.OK, withOld.StatusCode);
        }

        using (HttpResponseMessage withNew = await RequestTokenAsync(client, newClientId, newSecret))
        {
            Assert.Equal(HttpStatusCode.OK, withNew.StatusCode);
        }

        await RevokeCredentialAsync(oldClientId);

        using HttpResponseMessage afterRevocation =
            await RequestTokenAsync(client, oldClientId, oldSecret);

        // No grace period, no cache, no next-refresh. "We think this key leaked"
        // must not be followed by an interval in which it still works.
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevocation.StatusCode);

        using HttpResponseMessage newStillWorks =
            await RequestTokenAsync(client, newClientId, newSecret);

        Assert.Equal(HttpStatusCode.OK, newStillWorks.StatusCode);
    }

    [Fact]
    public async Task DisablingAnApplicationStopsIt()
    {
        using HttpClient client = factory.CreateClient();

        (Guid applicationId, string secret, string clientId) =
            await RegisterApplicationAsync("platform-administrator");

        await SetApplicationActiveAsync(applicationId, isActive: false);

        using HttpResponseMessage response = await RequestTokenAsync(client, clientId, secret);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ActingForSomebodyIsRefusedWithoutTheDelegationPermission()
    {
        using HttpClient client = factory.CreateClient();

        // A narrow role, holding one ordinary permission and not the delegation
        // one. This is the case the gate exists for: an application trusted to
        // read employees is not thereby trusted to read them *as* the finance
        // director.
        string roleCode = await CreateNarrowRoleAsync("platform.users.view");

        (_, string secret, string clientId) = await RegisterApplicationAsync(roleCode);

        Guid someone = await CreateUserAsync();

        using HttpResponseMessage response =
            await RequestTokenAsync(client, clientId, secret, onBehalfOf: someone);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("AUTHZ.DELEGATION_NOT_PERMITTED", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ActingForSomebodyWorksWhenTheApplicationIsTrustedWithIt()
    {
        using HttpClient client = factory.CreateClient();

        // The administrator role carries every declared permission, delegation
        // included — total authority is total, and an application granted it can
        // act as anybody. That is a consequence of granting it, not a gap.
        (_, string secret, string clientId) =
            await RegisterApplicationAsync("platform-administrator");

        Guid someone = await CreateUserAsync();

        using HttpResponseMessage response =
            await RequestTokenAsync(client, clientId, secret, onBehalfOf: someone);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("access_token").GetString()));
    }

    [Fact]
    public async Task ADelegatedCallCannotExceedWhatTheApplicationMayDo()
    {
        using HttpClient client = factory.CreateClient();

        // The application may delegate and may read users; it may not administer
        // roles. The person it acts for is an administrator who can. The call is
        // the intersection, so it reads and does not administer.
        string roleCode = await CreateNarrowRoleAsync(
            "platform.users.view", "platform.applications.act-on-behalf");

        (_, string secret, string clientId) = await RegisterApplicationAsync(roleCode);

        Guid administrator = await CreateUserAsync(administrator: true);

        string token = await GetTokenAsync(client, clientId, secret, onBehalfOf: administrator);

        using (HttpResponseMessage allowed = await SendAsync(
            client, token, HttpMethod.Get, "/api/v1/users?page=1&pageSize=1"))
        {
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using HttpResponseMessage refused = await SendAsync(
            client, token, HttpMethod.Get, "/api/v1/roles");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static async Task<HttpResponseMessage> RequestTokenAsync(
        HttpClient client, string clientId, string secret, Guid? onBehalfOf = null)
    {
        var fields = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = secret
        };

        if (onBehalfOf is { } subject)
        {
            fields["on_behalf_of"] = subject.ToString();
        }

        return await client.PostAsync(
            new Uri("/api/v1/oauth/token", UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    private static async Task<string> GetTokenAsync(
        HttpClient client, string clientId, string secret, Guid? onBehalfOf = null)
    {
        using HttpResponseMessage response =
            await RequestTokenAsync(client, clientId, secret, onBehalfOf);

        Assert.True(
            response.IsSuccessStatusCode,
            $"Token request failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return body.GetProperty("access_token").GetString()!;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, string token, HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, path);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await client.SendAsync(request);
    }

    private AuthorizationDbContext Authorization() =>
        new(new DbContextOptionsBuilder<AuthorizationDbContext>()
            .UseNpgsql(factory.TestConnectionString)
            .Options);

    /// <summary>
    /// An application with one credential, optionally holding a role.
    /// </summary>
    private async Task<(Guid ApplicationId, string Secret, string ClientId)>
        RegisterApplicationAsync(string? roleCode)
    {
        await using AuthorizationDbContext authorization = Authorization();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string code = $"test{Guid.CreateVersion7():N}"[..20];

        RegisteredApplication application =
            RegisteredApplication.Create(code, "Integration test", null, now).Value;

        authorization.Applications.Add(application);

        var hasher = new ApplicationSecretHasher();
        (string clientId, string secret, string hash) = hasher.Generate();

        authorization.ApplicationCredentials.Add(ApplicationCredential.Issue(
            application.Id, clientId, hash, "integration test", now, null).Value);

        if (roleCode is not null)
        {
            Role role = await authorization.Roles.FirstAsync(r => r.Code == roleCode);

            authorization.ApplicationAssignments.Add(ApplicationRoleAssignment.Grant(
                application.Id, role.Id, new GrantedScope(ScopeType.All, null),
                Guid.CreateVersion7(), now).Value);
        }

        await authorization.SaveChangesAsync();
        await BumpPermissionVersionAsync(authorization);

        return (application.Id, secret, clientId);
    }

    private async Task<(string Secret, string ClientId)> IssueSecondCredentialAsync(
        Guid applicationId)
    {
        await using AuthorizationDbContext authorization = Authorization();

        var hasher = new ApplicationSecretHasher();
        (string clientId, string secret, string hash) = hasher.Generate();

        authorization.ApplicationCredentials.Add(ApplicationCredential.Issue(
            applicationId, clientId, hash, "rotation", DateTimeOffset.UtcNow, null).Value);

        await authorization.SaveChangesAsync();

        return (secret, clientId);
    }

    private async Task RevokeCredentialAsync(string clientId)
    {
        await using AuthorizationDbContext authorization = Authorization();

        ApplicationCredential credential =
            await authorization.ApplicationCredentials.FirstAsync(c => c.ClientId == clientId);

        credential.Revoke(Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        await authorization.SaveChangesAsync();
    }

    private async Task SetApplicationActiveAsync(Guid applicationId, bool isActive)
    {
        await using AuthorizationDbContext authorization = Authorization();

        RegisteredApplication application =
            await authorization.Applications.FirstAsync(a => a.Id == applicationId);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (isActive)
        {
            application.Reactivate(now);
        }
        else
        {
            application.Deactivate(now);
        }

        await authorization.SaveChangesAsync();
        await BumpPermissionVersionAsync(authorization);
    }

    /// <summary>
    /// The permission cache is version-stamped, so a change made outside the API
    /// must bump the stamp or a cached empty set answers the next request.
    /// </summary>
    private static async Task BumpPermissionVersionAsync(AuthorizationDbContext authorization)
    {
        PermissionVersionRow version = await authorization.PermissionVersion.FirstAsync();

        version.Version++;
        version.UpdatedAt = DateTimeOffset.UtcNow;

        await authorization.SaveChangesAsync();
    }

    /// <summary>
    /// A role holding exactly the named permissions and nothing else.
    /// <para>
    /// Purpose-built per test. Reusing the administrator role would prove
    /// nothing about a gate, because it carries every permission there is.
    /// </para>
    /// </summary>
    private async Task<string> CreateNarrowRoleAsync(params string[] permissionNames)
    {
        await using AuthorizationDbContext authorization = Authorization();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string code = $"narrow{Guid.CreateVersion7():N}"[..24];

        Role role = Role.Create(code, "دور ضيق", "Narrow role", null, now).Value;

        foreach (string name in permissionNames)
        {
            Modules.Authorization.Domain.Permissions.Permission permission =
                await authorization.Permissions.FirstAsync(p => p.Name == name);

            role.AddPermission(permission.Id, permission.Name, now);
        }

        authorization.Roles.Add(role);

        await authorization.SaveChangesAsync();

        return code;
    }

    private async Task<Guid> CreateUserAsync(bool administrator = false)
    {
        await using var identity = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(factory.TestConnectionString)
                .Options);

        string username = $"obo{Guid.CreateVersion7():N}"[..20];

        User user = User.Create(
            username, $"{username}@example.invalid", "Delegation subject",
            DateTimeOffset.UtcNow, mustChangePassword: false).Value;

        var hasher = new Argon2PasswordHasher(Options.Create(new Argon2Options
        {
            MemoryKib = 8192,
            Iterations = 1,
            Parallelism = 1
        }));

        identity.Users.Add(user);
        identity.Credentials.Add(UserCredential.Create(
            user.Id, hasher.Hash(ValidPassword), hasher.AlgorithmId, DateTimeOffset.UtcNow));

        await identity.SaveChangesAsync();

        if (administrator)
        {
            await using AuthorizationDbContext authorization = Authorization();

            Role role = await authorization.Roles.FirstAsync(r => r.Code == "platform-administrator");

            authorization.Assignments.Add(UserRoleAssignment.GrantByPlatform(
                user.Id, role.Id, DateTimeOffset.UtcNow));

            await authorization.SaveChangesAsync();
            await BumpPermissionVersionAsync(authorization);
        }

        return user.Id;
    }
}
