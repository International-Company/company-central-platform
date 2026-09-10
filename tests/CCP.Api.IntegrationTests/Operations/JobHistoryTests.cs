using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CCP.Kernel.Application.Jobs;
using CCP.Kernel.Infrastructure.Jobs;
using CCP.Kernel.Infrastructure.Persistence;
using CCP.Modules.Authorization.Domain.Roles;
using CCP.Modules.Authorization.Domain.Scopes;
using CCP.Modules.Authorization.Infrastructure.Persistence;
using CCP.Modules.Identity.Domain.Credentials;
using CCP.Modules.Identity.Domain.Users;
using CCP.Modules.Identity.Infrastructure.Persistence;
using CCP.Modules.Identity.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CCP.Api.IntegrationTests.Operations;

/// <summary>
/// The job history, written and read against a real database.
/// <para>
/// <b>This exists because Phase 14 shipped an instrument nothing emitted.</b>
/// <c>ccp.jobs.runs</c> was declared, an alert was written against it, and the
/// method was in a project no background sweep referenced — so the alert sat
/// permanently green on jobs that might never have run. The unit tests cover the
/// runner's behaviour; these cover the half that only a database can answer:
/// that the row is really written, that the summary is really assembled from it,
/// and that a job which stopped running does not quietly vanish from the page.
/// </para>
/// </summary>
public sealed class JobHistoryTests(PlatformApiFactory factory)
    : IClassFixture<PlatformApiFactory>
{
    private const string ValidPassword = "correct-horse-battery-staple";

    [Fact]
    public async Task TheJournal_WritesTheRun()
    {
        var journal = factory.Services.GetRequiredService<IJobJournal>();

        DateTimeOffset startedAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        await journal.RecordAsync(new JobRun(
            "test.journal-writes",
            startedAt,
            DurationMs: 42,
            JobOutcome.Succeeded,
            Summary: "Removed 7 rows."));

        await using KernelDbContext kernel = Kernel();

        JobRunRecord row = await kernel.JobRuns
            .SingleAsync(run => run.Job == "test.journal-writes");

        Assert.Equal(JobOutcome.Succeeded, row.Outcome);
        Assert.Equal("Removed 7 rows.", row.Summary);
        Assert.Null(row.Error);
        Assert.Equal(42, row.DurationMs);

        // Which process ran it. On one instance this is noise; on two it is the
        // difference between "the sweep is failing" and "the sweep is failing on
        // one machine", which are different incidents.
        Assert.False(string.IsNullOrWhiteSpace(row.Instance));
    }

    /// <summary>
    /// The failure is what somebody comes looking for, so it is stored rather
    /// than only logged — and it carries the exception type, because "timeout"
    /// and "column does not exist" call for different people.
    /// </summary>
    [Fact]
    public async Task AFailedRun_KeepsWhatWentWrong()
    {
        var journal = factory.Services.GetRequiredService<IJobJournal>();

        await journal.RecordAsync(new JobRun(
            "test.journal-failure",
            DateTimeOffset.UtcNow,
            DurationMs: 3,
            JobOutcome.Failed,
            Summary: null,
            Error: "NpgsqlException: relation does not exist"));

        await using KernelDbContext kernel = Kernel();

        JobRunRecord row = await kernel.JobRuns
            .SingleAsync(run => run.Job == "test.journal-failure");

        Assert.Equal(JobOutcome.Failed, row.Outcome);
        Assert.Contains("relation does not exist", row.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The summary is built from the newest run of every job <i>plus</i> the
    /// last day's runs — not from the window alone.
    /// <para>
    /// <b>This is the assertion that matters most on the whole page.</b> A
    /// summary assembled only from a recent window would drop a job that stopped
    /// running a week ago, so the row would disappear from the screen rather
    /// than turn red. A job silently absent is exactly the failure the page was
    /// built to catch.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AJobThatStoppedRunning_StillAppears()
    {
        var journal = factory.Services.GetRequiredService<IJobJournal>();

        await journal.RecordAsync(new JobRun(
            "test.long-stopped",
            DateTimeOffset.UtcNow.AddDays(-9),
            DurationMs: 12,
            JobOutcome.Failed,
            Summary: null,
            Error: "InvalidOperationException: it broke and nobody noticed"));

        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await SignInWithOperationsAsync(client));

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/platform/jobs", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        JsonElement stopped = body
            .EnumerateArray()
            .Single(job => job.GetProperty("job").GetString() == "test.long-stopped");

        Assert.Equal("Failed", stopped.GetProperty("lastOutcome").GetString());

        // Nine days old, so nothing of it is inside the one-day window. The row
        // is present anyway, and reports zero recent runs — which is the honest
        // pair of facts: it last failed, and it has not run since.
        Assert.Equal(0, stopped.GetProperty("recentRuns").GetInt32());
        Assert.Contains(
            "nobody noticed",
            stopped.GetProperty("lastError").GetString(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The summary counts failures within the window and reports the newest run
    /// as the current state.
    /// </summary>
    [Fact]
    public async Task TheSummary_CountsRecentFailures()
    {
        var journal = factory.Services.GetRequiredService<IJobJournal>();

        DateTimeOffset now = DateTimeOffset.UtcNow;

        await journal.RecordAsync(new JobRun(
            "test.mixed", now.AddHours(-3), 10, JobOutcome.Failed, null, "boom"));

        await journal.RecordAsync(new JobRun(
            "test.mixed", now.AddHours(-2), 20, JobOutcome.Failed, null, "boom again"));

        await journal.RecordAsync(new JobRun(
            "test.mixed", now.AddHours(-1), 30, JobOutcome.Succeeded, "Removed 2 rows."));

        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await SignInWithOperationsAsync(client));

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/platform/jobs", UriKind.Relative));

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        JsonElement mixed = body
            .EnumerateArray()
            .Single(job => job.GetProperty("job").GetString() == "test.mixed");

        Assert.Equal(3, mixed.GetProperty("recentRuns").GetInt32());
        Assert.Equal(2, mixed.GetProperty("recentFailures").GetInt32());

        // The newest run decides the state shown, and it succeeded — a job that
        // failed twice and then recovered is not currently failing.
        Assert.Equal("Succeeded", mixed.GetProperty("lastOutcome").GetString());
        Assert.Equal("Removed 2 rows.", mixed.GetProperty("lastSummary").GetString());

        // The average over the window, not the last duration: one slow pass is
        // weather, and a job that has doubled since Tuesday is the thing to see.
        Assert.Equal(20d, mixed.GetProperty("averageDurationMs").GetDouble(), 3);
    }

    [Fact]
    public async Task TheRunHistory_ReturnsOneJobsRunsNewestFirst()
    {
        var journal = factory.Services.GetRequiredService<IJobJournal>();

        DateTimeOffset now = DateTimeOffset.UtcNow;

        await journal.RecordAsync(new JobRun("test.ordering", now.AddMinutes(-30), 1, JobOutcome.Succeeded, "older"));
        await journal.RecordAsync(new JobRun("test.ordering", now.AddMinutes(-10), 1, JobOutcome.Succeeded, "newer"));

        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await SignInWithOperationsAsync(client));

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/platform/jobs/test.ordering/runs", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement[] runs = [.. body.EnumerateArray()];

        Assert.Equal(2, runs.Length);
        Assert.Equal("newer", runs[0].GetProperty("summary").GetString());
        Assert.Equal("older", runs[1].GetProperty("summary").GetString());
    }

    /// <summary>
    /// Whether the machinery is working is not public.
    /// <para>
    /// It reveals the Platform's shape and its weak moments — which sweeps
    /// exist, when they run, and which of them is currently failing.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheEndpoints_RefuseAnAnonymousCaller()
    {
        using HttpClient client = factory.CreateClient();

        foreach (string path in new[]
                 {
                     "/api/v1/platform/jobs",
                     "/api/v1/platform/jobs/documents.purge/runs",
                     "/api/v1/platform/outbox"
                 })
        {
            using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    /// <summary>
    /// Signing in is not enough. Seeing the machinery is its own permission,
    /// kept apart from the audit trail so that handing somebody the dashboard
    /// does not hand them everybody's activity.
    /// </summary>
    [Fact]
    public async Task TheEndpoints_RefuseACallerWithoutThePermission()
    {
        using HttpClient client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await SignInAsync(client, grantOperations: false));

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/platform/jobs", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// The outbox panel answers with the age of the oldest undelivered message,
    /// which is the figure that distinguishes a busy minute from a relay that
    /// stopped on Sunday.
    /// </summary>
    [Fact]
    public async Task TheOutboxDepth_IsReadable()
    {
        using HttpClient client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await SignInWithOperationsAsync(client));

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/platform/outbox", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("pending").GetInt32() >= 0);
        Assert.True(body.GetProperty("deadLettered").GetInt32() >= 0);
    }

    private KernelDbContext Kernel() =>
        new(new DbContextOptionsBuilder<KernelDbContext>()
            .UseNpgsql(factory.TestConnectionString)
            .Options);

    private async Task<string> SignInWithOperationsAsync(HttpClient client) =>
        await SignInAsync(client, grantOperations: true);

    /// <summary>Creates an account, optionally grants it the permission, and signs it in.</summary>
    private async Task<string> SignInAsync(HttpClient client, bool grantOperations)
    {
        string username = $"ops{Guid.CreateVersion7():N}"[..20];

        await using var identity = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(factory.TestConnectionString)
                .Options);

        User user = User.Create(
            username, $"{username}@example.invalid", "Operations", DateTimeOffset.UtcNow,
            mustChangePassword: false).Value;

        // Reduced cost, like the other suites: these tests are about the
        // history, and the production hashing parameters are exercised in the
        // unit tests.
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

        if (grantOperations)
        {
            await GrantOperationsAsync(user.Id);
        }

        using HttpResponseMessage login = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { username, password = ValidPassword });

        Assert.True(login.IsSuccessStatusCode, $"Sign-in failed: {login.StatusCode}.");

        JsonElement body = await login.Content.ReadFromJsonAsync<JsonElement>();

        return body.GetProperty("accessToken").GetString()!;
    }

    /// <summary>
    /// Grants <c>platform.operations.view</c> and nothing else.
    /// <para>
    /// <b>A purpose-built role rather than the administrator's.</b> The seeded
    /// administrator carries every declared permission, so it would pass the
    /// refusal test above for the wrong reason and prove nothing about this
    /// permission existing at all.
    /// </para>
    /// </summary>
    private async Task GrantOperationsAsync(Guid userId)
    {
        await using var authorization = new AuthorizationDbContext(
            new DbContextOptionsBuilder<AuthorizationDbContext>()
                .UseNpgsql(factory.TestConnectionString)
                .Options);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        Role? role = await authorization.Roles
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Code == "operations-test-viewer");

        if (role is null)
        {
            role = Role.Create(
                "operations-test-viewer", "مُشاهد التشغيل", "Operations viewer",
                "Sees the Platform's own machinery, and nothing else.", now).Value;

            Modules.Authorization.Domain.Permissions.Permission permission =
                await authorization.Permissions
                    .SingleAsync(p => p.Name == "platform.operations.view");

            role.AddPermission(permission.Id, permission.Name, now);

            authorization.Roles.Add(role);
            await authorization.SaveChangesAsync();
        }

        authorization.Assignments.Add(UserRoleAssignment.Grant(
            userId, role.Id, new GrantedScope(ScopeType.All, null),
            Guid.CreateVersion7(), now).Value);

        await authorization.SaveChangesAsync();

        // The permission cache is version-stamped, so a grant made outside the
        // API must bump the stamp or the new token resolves against a cached
        // empty set.
        PermissionVersionRow version = await authorization.PermissionVersion.FirstAsync();

        version.Version++;

        await authorization.SaveChangesAsync();
    }
}
