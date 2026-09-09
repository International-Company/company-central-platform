using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CCP.Modules.Identity.Domain.Credentials;
using CCP.Modules.Identity.Domain.Users;
using CCP.Modules.Identity.Infrastructure.Persistence;
using CCP.Modules.Identity.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CCP.Api.IntegrationTests.Workflow;

/// <summary>
/// A whole approval, against a real database.
/// <para>
/// <b>The acceptance criterion for Phase 8 is that a multi-step approval runs
/// end to end with no Platform code specific to any business process.</b> This
/// suite is that criterion, executed: it registers a process the Platform has
/// never heard of, starts a request against a record the Platform cannot read,
/// and walks it to a decision.
/// </para>
/// <para>
/// It also covers the two refusals that matter most — a version that changed
/// underneath a running instance must not affect it, and somebody who is not the
/// assignee must not be able to act.
/// </para>
/// </summary>
public sealed class ApprovalCycleTests(PlatformApiFactory factory)
    : IClassFixture<PlatformApiFactory>
{
    private const string ValidPassword = "correct-horse-battery-staple";

    [Fact]
    public async Task ARegisteredProcessRunsToADecision()
    {
        using HttpClient client = factory.CreateClient();

        (string token, Guid userId) = await SignInAsync(client);

        string code = $"cycle-{Guid.CreateVersion7():N}"[..20];

        // A process that names one person: this test's own account. Nothing here
        // is specific to what is being approved, which is the point — the same
        // definition would serve a purchase, a leave request or a contract.
        using HttpResponseMessage registered = await SendAsync(
            client, token, HttpMethod.Post, "/api/v1/workflow/definitions",
            new
            {
                applicationCode = "testing",
                code,
                version = 1,
                nameAr = "دورة",
                nameEn = "Cycle",
                initialStepKey = "review",
                steps = new object[]
                {
                    new
                    {
                        key = "review",
                        nameAr = "مراجعة",
                        nameEn = "Review",
                        order = 1,
                        assigneeStrategy = "User",
                        assigneeUserId = userId,
                        transitions = new object[]
                        {
                            new { action = "Approve", targetStepKey = "approval" },
                            new { action = "Reject", targetStepKey = (string?)null }
                        }
                    },
                    new
                    {
                        key = "approval",
                        nameAr = "اعتماد",
                        nameEn = "Approval",
                        order = 2,
                        assigneeStrategy = "User",
                        assigneeUserId = userId,
                        transitions = new object[]
                        {
                            new { action = "Approve", targetStepKey = (string?)null },
                            new { action = "Return", targetStepKey = "review" }
                        }
                    }
                }
            });

        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        // A record the Platform has never heard of and cannot read.
        string resourceId = Guid.CreateVersion7().ToString();

        using HttpResponseMessage started = await SendAsync(
            client, token, HttpMethod.Post, "/api/v1/workflow/instances",
            new
            {
                applicationCode = "testing",
                definitionCode = code,
                resourceType = "test-record",
                resourceId
            });

        Assert.Equal(HttpStatusCode.Created, started.StatusCode);

        JsonElement instance = await started.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("review", instance.GetProperty("currentStepKey").GetString());
        Assert.Equal("Running", instance.GetProperty("status").GetString());

        // Step one: the inbox has it, and it says what can be done.
        Guid firstTask = await SingleTaskAsync(client, token);

        using HttpResponseMessage first = await SendAsync(
            client, token, HttpMethod.Post,
            $"/api/v1/me/tasks/{firstTask}/actions",
            new { action = "Approve", comment = "Looks right." });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        JsonElement afterFirst = await first.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("approval", afterFirst.GetProperty("currentStepKey").GetString());
        Assert.Equal("Running", afterFirst.GetProperty("status").GetString());

        // Step two ends it. This is the whole cycle: Create → Review → Approval.
        Guid secondTask = await SingleTaskAsync(client, token);

        Assert.NotEqual(firstTask, secondTask);

        using HttpResponseMessage second = await SendAsync(
            client, token, HttpMethod.Post,
            $"/api/v1/me/tasks/{secondTask}/actions",
            new { action = "Approve" });

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        JsonElement finished = await second.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Approved", finished.GetProperty("status").GetString());
        Assert.Null(finished.GetProperty("currentStepKey").GetString());

        // Every action recorded, in order, with who and when.
        JsonElement actions = finished.GetProperty("actions");

        Assert.Equal(2, actions.GetArrayLength());
        Assert.Equal("Looks right.", actions[0].GetProperty("comment").GetString());
        Assert.Equal(userId, actions[0].GetProperty("actorUserId").GetGuid());

        // And the inbox is empty again: nothing is left pending on a finished
        // request, which is what stops an inbox filling with work nobody can do.
        Assert.Empty(await PendingTaskIdsAsync(client, token));
    }

    [Fact]
    public async Task ANewVersionDoesNotDisturbARunningInstance()
    {
        using HttpClient client = factory.CreateClient();

        (string token, Guid userId) = await SignInAsync(client);

        string code = $"pin-{Guid.CreateVersion7():N}"[..18];

        await RegisterOneStepAsync(client, token, code, version: 1, userId, "first");

        using HttpResponseMessage started = await SendAsync(
            client, token, HttpMethod.Post, "/api/v1/workflow/instances",
            new
            {
                applicationCode = "testing",
                definitionCode = code,
                resourceType = "test-record",
                resourceId = Guid.CreateVersion7().ToString()
            });

        Assert.Equal(HttpStatusCode.Created, started.StatusCode);

        JsonElement instance = await started.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, instance.GetProperty("definitionVersion").GetInt32());
        Assert.Equal("first", instance.GetProperty("currentStepKey").GetString());

        // A second version, with a differently named step. The running instance
        // must not notice: an approval whose rules changed underneath it is an
        // approval nobody can account for.
        await RegisterOneStepAsync(client, token, code, version: 2, userId, "renamed");

        using HttpResponseMessage reread = await SendAsync(
            client, token, HttpMethod.Get,
            $"/api/v1/workflow/instances/{instance.GetProperty("id").GetGuid()}");

        JsonElement current = await reread.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, current.GetProperty("definitionVersion").GetInt32());
        Assert.Equal("first", current.GetProperty("currentStepKey").GetString());

        // And it can still be completed, on the version it started with.
        Guid task = await SingleTaskAsync(client, token);

        using HttpResponseMessage acted = await SendAsync(
            client, token, HttpMethod.Post,
            $"/api/v1/me/tasks/{task}/actions", new { action = "Approve" });

        Assert.Equal(HttpStatusCode.OK, acted.StatusCode);
    }

    [Fact]
    public async Task SomebodyElsesTaskCannotBeActedOn()
    {
        using HttpClient client = factory.CreateClient();

        (string ownerToken, Guid ownerId) = await SignInAsync(client);
        (string strangerToken, _) = await SignInAsync(client);

        string code = $"guard-{Guid.CreateVersion7():N}"[..18];

        await RegisterOneStepAsync(client, ownerToken, code, version: 1, ownerId, "only");

        using HttpResponseMessage started = await SendAsync(
            client, ownerToken, HttpMethod.Post, "/api/v1/workflow/instances",
            new
            {
                applicationCode = "testing",
                definitionCode = code,
                resourceType = "test-record",
                resourceId = Guid.CreateVersion7().ToString()
            });

        Assert.Equal(HttpStatusCode.Created, started.StatusCode);

        Guid task = await SingleTaskAsync(client, ownerToken);

        using HttpResponseMessage refused = await SendAsync(
            client, strangerToken, HttpMethod.Post,
            $"/api/v1/me/tasks/{task}/actions", new { action = "Approve" });

        // The rule whose failure means somebody approved something that was
        // never theirs. Checked in the handler and again in the aggregate,
        // because one check is one place to forget it.
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        // And the request has not moved.
        using HttpResponseMessage reread = await SendAsync(
            client, ownerToken, HttpMethod.Get,
            $"/api/v1/workflow/instances/{(await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid()}");

        JsonElement instance = await reread.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Running", instance.GetProperty("status").GetString());
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static async Task RegisterOneStepAsync(
        HttpClient client, string token, string code, int version, Guid assignee, string stepKey)
    {
        using HttpResponseMessage response = await SendAsync(
            client, token, HttpMethod.Post, "/api/v1/workflow/definitions",
            new
            {
                applicationCode = "testing",
                code,
                version,
                nameAr = "عملية",
                nameEn = "Process",
                initialStepKey = stepKey,
                steps = new object[]
                {
                    new
                    {
                        key = stepKey,
                        nameAr = "خطوة",
                        nameEn = "Step",
                        order = 1,
                        assigneeStrategy = "User",
                        assigneeUserId = assignee,
                        transitions = new object[]
                        {
                            new { action = "Approve", targetStepKey = (string?)null }
                        }
                    }
                }
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<Guid> SingleTaskAsync(HttpClient client, string token)
    {
        IReadOnlyList<Guid> pending = await PendingTaskIdsAsync(client, token);

        return Assert.Single(pending);
    }

    private static async Task<IReadOnlyList<Guid>> PendingTaskIdsAsync(
        HttpClient client, string token)
    {
        using HttpResponseMessage response = await SendAsync(
            client, token, HttpMethod.Get, "/api/v1/me/tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return [.. body.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())];
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, string token, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));

        request.Headers.Authorization = new("Bearer", token);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    /// <summary>Creates an account, grants it everything, and signs it in.</summary>
    private async Task<(string Token, Guid UserId)> SignInAsync(HttpClient client)
    {
        string username = $"wf{Guid.CreateVersion7():N}"[..20];

        await using var identity = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(factory.TestConnectionString)
                .Options);

        User user = User.Create(
            username, $"{username}@example.invalid", "Workflow", DateTimeOffset.UtcNow,
            mustChangePassword: false).Value;

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

        // The administrator role, so this account can register definitions and
        // read instances. Granted directly rather than through the API, because
        // granting demands a second factor and that is a different test.
        await GrantAdministratorAsync(user.Id);

        using HttpResponseMessage login = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { username, password = ValidPassword });

        Assert.True(login.IsSuccessStatusCode, $"Sign-in failed: {login.StatusCode}.");

        JsonElement body = await login.Content.ReadFromJsonAsync<JsonElement>();

        return (body.GetProperty("accessToken").GetString()!, user.Id);
    }

    private async Task GrantAdministratorAsync(Guid userId)
    {
        await using var authorization =
            new Modules.Authorization.Infrastructure.Persistence.AuthorizationDbContext(
                new DbContextOptionsBuilder<
                    Modules.Authorization.Infrastructure.Persistence.AuthorizationDbContext>()
                    .UseNpgsql(factory.TestConnectionString)
                    .Options);

        Modules.Authorization.Domain.Roles.Role role =
            await authorization.Roles.FirstAsync(r => r.Code == "platform-administrator");

        authorization.Assignments.Add(
            Modules.Authorization.Domain.Roles.UserRoleAssignment.GrantByPlatform(
                userId, role.Id, DateTimeOffset.UtcNow));

        await authorization.SaveChangesAsync();

        // The permission cache is version-stamped, so a grant made outside the
        // API must bump the stamp or the new token resolves against a cached
        // empty set — which is the shape of a defect that cost this project a
        // day, and is worth not reproducing in a test.
        Modules.Authorization.Infrastructure.Persistence.PermissionVersionRow version =
            await authorization.PermissionVersion.FirstAsync();

        version.Version++;
        version.UpdatedAt = DateTimeOffset.UtcNow;

        await authorization.SaveChangesAsync();
    }
}
