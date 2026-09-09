using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CCP.Modules.Authorization.Domain.Roles;
using CCP.Modules.Authorization.Domain.Scopes;
using CCP.Modules.Authorization.Infrastructure.Persistence;
using CCP.Modules.Identity.Domain.Credentials;
using CCP.Modules.Identity.Domain.Users;
using CCP.Modules.Identity.Infrastructure.Persistence;
using CCP.Modules.Identity.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CCP.Api.IntegrationTests.Documents;

/// <summary>
/// Storing a file, and who can get it back.
/// <para>
/// <b>The acceptance criterion for Phase 10 is that access is decided by the
/// Platform and not by the storage.</b> These tests are that criterion executed:
/// two accounts holding exactly the same permission see different documents,
/// and the difference is made by access rules alone.
/// </para>
/// <para>
/// Neither account is an administrator. That is deliberate and is the whole
/// design of the suite — a company-wide grant reaches every document by
/// definition, so testing with one would prove nothing about the rules.
/// </para>
/// </summary>
public sealed class DocumentAccessTests(PlatformApiFactory factory)
    : IClassFixture<PlatformApiFactory>
{
    private const string ValidPassword = "correct-horse-battery-staple";

    private static readonly byte[] APdf =
        Encoding.UTF8.GetBytes("%PDF-1.7\nA short but entirely genuine document.\n");

    [Fact]
    public async Task AFileGoesInAndComesBackOut()
    {
        using HttpClient client = factory.CreateClient();

        (string token, _) = await SignInAsync(client);

        JsonElement document = await UploadAsync(client, token, APdf, "contract.pdf");
        Guid id = document.GetProperty("id").GetGuid();

        Assert.Equal("application/pdf", document.GetProperty("contentType").GetString());
        Assert.Equal(1, document.GetProperty("currentVersionNumber").GetInt32());

        // The uploader owns it, and owning it means managing it — without any
        // rule having been written.
        Assert.Equal("Manage", document.GetProperty("accessLevel").GetString());

        using HttpResponseMessage content = await SendAsync(
            client, token, HttpMethod.Get, $"/api/v1/documents/{id}/content");

        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal(APdf, await content.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AProgramRenamedToPdfIsRefused()
    {
        using HttpClient client = factory.CreateClient();

        (string token, _) = await SignInAsync(client);

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("MZ\0\0this is an executable"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "quarterly-report.pdf");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documents")
        {
            Content = form
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("DOCUMENTS.FILE_TYPE_NOT_ALLOWED", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SomebodyElseCannotReadItUntilItIsSharedWithThem()
    {
        using HttpClient client = factory.CreateClient();

        (string ownerToken, _) = await SignInAsync(client);
        (string otherToken, Guid otherUserId) = await SignInAsync(client);

        Guid id = (await UploadAsync(client, ownerToken, APdf, "salary-letter.pdf"))
            .GetProperty("id").GetGuid();

        // Same permission, same everything, different person.
        using HttpResponseMessage refused = await SendAsync(
            client, otherToken, HttpMethod.Get, $"/api/v1/documents/{id}");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        using HttpResponseMessage shared = await SendAsync(
            client, ownerToken, HttpMethod.Put, $"/api/v1/documents/{id}/access",
            new { subjectKind = "User", subjectId = otherUserId, level = "Read" });

        Assert.Equal(HttpStatusCode.OK, shared.StatusCode);

        using HttpResponseMessage allowed = await SendAsync(
            client, otherToken, HttpMethod.Get, $"/api/v1/documents/{id}");

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        JsonElement asRead = await allowed.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Read", asRead.GetProperty("accessLevel").GetString());

        // Read is read. It does not stretch to deleting somebody else's file.
        using HttpResponseMessage deletion = await SendAsync(
            client, otherToken, HttpMethod.Delete, $"/api/v1/documents/{id}");

        Assert.Equal(HttpStatusCode.Forbidden, deletion.StatusCode);
    }

    [Fact]
    public async Task TheRefusalsAreInTheAccessLogAsWellAsTheSuccesses()
    {
        using HttpClient client = factory.CreateClient();

        (string ownerToken, _) = await SignInAsync(client);
        (string otherToken, Guid otherUserId) = await SignInAsync(client);

        Guid id = (await UploadAsync(client, ownerToken, APdf, "policy.pdf"))
            .GetProperty("id").GetGuid();

        using (HttpResponseMessage attempt = await SendAsync(
            client, otherToken, HttpMethod.Get, $"/api/v1/documents/{id}"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, attempt.StatusCode);
        }

        using HttpResponseMessage log = await SendAsync(
            client, ownerToken, HttpMethod.Get, $"/api/v1/documents/{id}/access-log");

        Assert.Equal(HttpStatusCode.OK, log.StatusCode);

        JsonElement body = await log.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement[] entries = [.. body.GetProperty("items").EnumerateArray()];

        // One person failing to open a document is a wrong link; one person
        // failing to open forty is something else. A log of successes only shows
        // neither.
        Assert.Contains(entries, e =>
            !e.GetProperty("wasAllowed").GetBoolean()
            && e.GetProperty("actorUserId").GetGuid() == otherUserId);

        Assert.Contains(entries, e =>
            e.GetProperty("wasAllowed").GetBoolean()
            && e.GetProperty("action").GetString() == "Upload");
    }

    [Fact]
    public async Task DeletingHidesTheDocumentAndRestoringBringsItBack()
    {
        using HttpClient client = factory.CreateClient();

        (string token, _) = await SignInAsync(client);

        string title = $"Deletable {Guid.CreateVersion7():N}";
        Guid id = (await UploadAsync(client, token, APdf, "temporary.pdf", title))
            .GetProperty("id").GetGuid();

        using (HttpResponseMessage deleted = await SendAsync(
            client, token, HttpMethod.Delete, $"/api/v1/documents/{id}"))
        {
            Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        }

        Assert.False(await AppearsInSearchAsync(client, token, title, includeDeleted: false));

        // Marked, not gone: the content is untouched and the record is findable
        // by anybody who goes looking for it.
        Assert.True(await AppearsInSearchAsync(client, token, title, includeDeleted: true));

        using (HttpResponseMessage restored = await SendAsync(
            client, token, HttpMethod.Post, $"/api/v1/documents/{id}/restore"))
        {
            Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        }

        Assert.True(await AppearsInSearchAsync(client, token, title, includeDeleted: false));
    }

    [Fact]
    public async Task ANewVersionLeavesTheOldOneWhereItWas()
    {
        using HttpClient client = factory.CreateClient();

        (string token, _) = await SignInAsync(client);

        Guid id = (await UploadAsync(client, token, APdf, "contract.pdf"))
            .GetProperty("id").GetGuid();

        byte[] revised = Encoding.UTF8.GetBytes("%PDF-1.7\nThe revised text.\n");

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(revised);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "contract-v2.pdf");
        form.Add(new StringContent("Legal asked for a change"), "notes");

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/documents/{id}/versions")
        {
            Content = form
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement updated = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2, updated.GetProperty("currentVersionNumber").GetInt32());

        // The point of versioning: somebody signed version one, and it is still
        // there, byte for byte.
        using HttpResponseMessage first = await SendAsync(
            client, token, HttpMethod.Get, $"/api/v1/documents/{id}/content?version=1");

        Assert.Equal(APdf, await first.Content.ReadAsByteArrayAsync());

        using HttpResponseMessage current = await SendAsync(
            client, token, HttpMethod.Get, $"/api/v1/documents/{id}/content");

        Assert.Equal(revised, await current.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ADocumentCanBeFiledAgainstARecordThePlatformKnowsNothingAbout()
    {
        using HttpClient client = factory.CreateClient();

        (string token, _) = await SignInAsync(client);

        Guid id = (await UploadAsync(client, token, APdf, "signed-order.pdf"))
            .GetProperty("id").GetGuid();

        string resourceId = $"PO-{Guid.CreateVersion7():N}"[..12];

        using (HttpResponseMessage linked = await SendAsync(
            client, token, HttpMethod.Post, $"/api/v1/documents/{id}/links",
            new { resourceType = "purchase-order", resourceId }))
        {
            Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        }

        using HttpResponseMessage filed = await SendAsync(
            client, token, HttpMethod.Get,
            $"/api/v1/resources/purchase-order/{resourceId}/documents");

        Assert.Equal(HttpStatusCode.OK, filed.StatusCode);

        JsonElement[] documents = [.. (await filed.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray()];

        Assert.Single(documents);
        Assert.Equal(id, documents[0].GetProperty("id").GetGuid());
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static async Task<bool> AppearsInSearchAsync(
        HttpClient client, string token, string title, bool includeDeleted)
    {
        using HttpResponseMessage response = await SendAsync(
            client, token, HttpMethod.Get,
            $"/api/v1/documents?term={Uri.EscapeDataString(title)}&includeDeleted={includeDeleted}");

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return body.GetProperty("items").EnumerateArray().Any(
            item => item.GetProperty("title").GetString() == title);
    }

    private static async Task<JsonElement> UploadAsync(
        HttpClient client, string token, byte[] content, string fileName, string? title = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", fileName);

        if (title is not null)
        {
            form.Add(new StringContent(title), "title");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documents")
        {
            Content = form
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.True(
            response.IsSuccessStatusCode,
            $"Upload failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, string token, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    /// <summary>
    /// An account that may use documents and reaches no further than itself.
    /// </summary>
    private async Task<(string Token, Guid UserId)> SignInAsync(HttpClient client)
    {
        string username = $"doc{Guid.CreateVersion7():N}"[..20];

        await using var identity = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(factory.TestConnectionString)
                .Options);

        User user = User.Create(
            username, $"{username}@example.invalid", "Documents", DateTimeOffset.UtcNow,
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

        await GrantDocumentUserAsync(user.Id);

        using HttpResponseMessage login = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { username, password = ValidPassword });

        Assert.True(login.IsSuccessStatusCode, $"Sign-in failed: {login.StatusCode}.");

        JsonElement body = await login.Content.ReadFromJsonAsync<JsonElement>();

        return (body.GetProperty("accessToken").GetString()!, user.Id);
    }

    /// <summary>
    /// Grants every documents permission at <c>Self</c> scope.
    /// <para>
    /// <b>Self, not All.</b> A company-wide grant makes the access rules
    /// irrelevant by design, so an administrator would pass every one of these
    /// tests without a single rule being correct.
    /// </para>
    /// </summary>
    private async Task GrantDocumentUserAsync(Guid userId)
    {
        await using var authorization = new AuthorizationDbContext(
            new DbContextOptionsBuilder<AuthorizationDbContext>()
                .UseNpgsql(factory.TestConnectionString)
                .Options);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        Role? role = await authorization.Roles
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Code == "documents-test-user");

        if (role is null)
        {
            role = Role.Create(
                "documents-test-user", "مستخدم مستندات", "Documents user",
                "Every documents permission, and no reach beyond oneself.", now).Value;

            List<Modules.Authorization.Domain.Permissions.Permission> permissions =
                await authorization.Permissions
                    .Where(p => p.Name.StartsWith("platform.documents."))
                    .ToListAsync();

            Assert.NotEmpty(permissions);

            foreach (Modules.Authorization.Domain.Permissions.Permission permission in permissions)
            {
                role.AddPermission(permission.Id, permission.Name, now);
            }

            authorization.Roles.Add(role);
            await authorization.SaveChangesAsync();
        }

        authorization.Assignments.Add(UserRoleAssignment.Grant(
            userId, role.Id, new GrantedScope(ScopeType.Self, null),
            Guid.CreateVersion7(), now).Value);

        await authorization.SaveChangesAsync();

        // The permission cache is version-stamped, so a grant made outside the
        // API must bump the stamp or the new token resolves against a cached
        // empty set.
        PermissionVersionRow version = await authorization.PermissionVersion.FirstAsync();

        version.Version++;
        version.UpdatedAt = now;

        await authorization.SaveChangesAsync();
    }
}
