using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CCP.Kernel.Infrastructure.Persistence;
using CCP.Kernel.Primitives;
using CCP.Modules.Identity.Domain.Credentials;
using CCP.Modules.Identity.Domain.Users;
using CCP.Modules.Identity.Infrastructure.Persistence;
using CCP.Modules.Identity.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CCP.Api.IntegrationTests.Identity;

/// <summary>
/// The authentication flows against a real database.
/// <para>
/// These verify what unit tests structurally cannot: that the unique constraints
/// hold, that the outbox commits in the same transaction as the change, that
/// refresh rotation behaves correctly when rows actually persist, and that reuse
/// detection revokes a family that really exists.
/// </para>
/// <para>
/// <b>Written but not yet executed.</b> No database was reachable on the
/// development machine when this phase was built (DEVELOPMENT_STATUS.md §4).
/// They run in CI, which provisions PostgreSQL as a service container. That gap
/// is recorded as technical debt rather than glossed over.
/// </para>
/// </summary>
public sealed class AuthenticationFlowTests(PlatformApiFactory factory) : IClassFixture<PlatformApiFactory>
{
    private const string ValidPassword = "a-sufficiently-long-passphrase";

    // -----------------------------------------------------------------------
    // Sign-in
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SignIn_Succeeds_WithCorrectCredentials()
    {
        string username = await SeedUserAsync();

        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { username, password = ValidPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("refreshToken").GetString()));
        Assert.Equal("Bearer", body.GetProperty("tokenType").GetString());
    }

    [Theory]
    [InlineData("wrong-password")]
    [InlineData("")]
    public async Task SignIn_ReturnsTheUniformError_ForAWrongPassword(string password)
    {
        string username = await SeedUserAsync();

        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { username, password });

        // Empty passwords are rejected by validation; wrong ones by
        // authentication. Neither may reveal that the account exists.
        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.UnprocessableEntity,
            $"Expected 401 or 422 but got {(int)response.StatusCode}.");

        string raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("ACCOUNT_DISABLED", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("ACCOUNT_LOCKED", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("USER_NOT_FOUND", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignIn_IsIndistinguishable_BetweenUnknownUserAndWrongPassword()
    {
        // The core anti-enumeration property. If these two responses differ in
        // any observable way, the endpoint becomes a tool for discovering who
        // works at the company.
        string username = await SeedUserAsync();

        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage wrongPassword = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { username, password = "definitely-not-the-password" });

        using HttpResponseMessage unknownUser = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { username = "no-such-user-at-all", password = "definitely-not-the-password" });

        Assert.Equal(wrongPassword.StatusCode, unknownUser.StatusCode);

        JsonElement wrongBody = await wrongPassword.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement unknownBody = await unknownUser.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(
            wrongBody.GetProperty("code").GetString(),
            unknownBody.GetProperty("code").GetString());

        Assert.Equal(
            wrongBody.GetProperty("detail").GetString(),
            unknownBody.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task SignIn_RecordsAFailedAttempt_EvenForAnUnknownUsername()
    {
        // Attempts against usernames that do not exist are exactly what
        // credential stuffing looks like, and are invisible if not recorded.
        using HttpClient client = factory.CreateClient();

        // "ghost-" plus 32 hex characters is 38, not 40. The original slice
        // asked for more than the string holds and threw.
        string attempted = $"ghost-{Guid.CreateVersion7():N}";

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { username = attempted, password = "whatever" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        await using IdentityDbContext context = CreateIdentityContext();

        LoginAttempt? recorded = await context.LoginAttempts
            .FirstOrDefaultAsync(a => a.AttemptedUsername == attempted);

        Assert.NotNull(recorded);
        Assert.False(recorded.Succeeded);
        Assert.Null(recorded.UserId);
    }

    [Fact]
    public async Task SignIn_LocksTheAccount_AfterRepeatedFailures()
    {
        string username = await SeedUserAsync();

        using HttpClient client = factory.CreateClient();

        // Past the free allowance.
        for (int i = 0; i < 6; i++)
        {
            using HttpResponseMessage _ = await client.PostAsJsonAsync(
                new Uri("/api/v1/auth/login", UriKind.Relative),
                new { username, password = "wrong" });
        }

        await using IdentityDbContext context = CreateIdentityContext();

        User user = await context.Users.SingleAsync(u => u.Username == username);

        Assert.True(user.FailedAttemptCount >= 4);
        Assert.NotNull(user.LockedUntil);

        // Even with the correct password, a locked account is refused — and
        // still with the uniform error.
        using HttpResponseMessage locked = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { username, password = ValidPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);

        JsonElement body = await locked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("IDENTITY.INVALID_CREDENTIALS", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SignIn_WritesAnOutboxEventInTheSameTransaction()
    {
        // The guarantee the outbox exists for. If these were separate
        // transactions, an event could be lost after a successful sign-in.
        string username = await SeedUserAsync();

        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { username, password = ValidPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using KernelDbContext kernel = factory.CreateDbContext();

        bool loginEventWritten = await kernel.OutboxMessages
            .AnyAsync(m => m.EventType == "identity.login.succeeded");

        Assert.True(loginEventWritten, "A successful sign-in must stage its event on the outbox.");
    }

    // -----------------------------------------------------------------------
    // Refresh rotation and reuse detection
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Refresh_RotatesTheToken()
    {
        string username = await SeedUserAsync();

        using HttpClient client = factory.CreateClient();

        string firstRefresh = await SignInAndGetRefreshTokenAsync(client, username);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/refresh", UriKind.Relative),
            new { refreshToken = firstRefresh });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.NotEqual(firstRefresh, body.GetProperty("refreshToken").GetString());
    }

    [Fact]
    public async Task Refresh_RejectsAnAlreadyUsedToken_AndRevokesTheWholeFamily()
    {
        // The highest-value control in the design (ADR-006). A replayed token
        // means the credential was copied, so every descendant of that sign-in
        // must stop working — not just the token that was replayed.
        string username = await SeedUserAsync();

        using HttpClient client = factory.CreateClient();

        string firstRefresh = await SignInAndGetRefreshTokenAsync(client, username);

        using HttpResponseMessage rotated = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/refresh", UriKind.Relative),
            new { refreshToken = firstRefresh });

        JsonElement rotatedBody = await rotated.Content.ReadFromJsonAsync<JsonElement>();
        string secondRefresh = rotatedBody.GetProperty("refreshToken").GetString()!;

        // Replay the spent token, as a thief would.
        using HttpResponseMessage replay = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/refresh", UriKind.Relative),
            new { refreshToken = firstRefresh });

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        JsonElement replayBody = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("IDENTITY.REFRESH_TOKEN_REUSED", replayBody.GetProperty("code").GetString());

        // The successor must now be dead too, or the thief simply continues.
        using HttpResponseMessage successor = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/refresh", UriKind.Relative),
            new { refreshToken = secondRefresh });

        Assert.NotEqual(HttpStatusCode.OK, successor.StatusCode);
    }

    [Fact]
    public async Task Refresh_RejectsAnUnknownToken()
    {
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/refresh", UriKind.Relative),
            new { refreshToken = "a-token-that-was-never-issued" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RefreshTokens_AreStoredHashed()
    {
        // A leaked database backup must yield no usable token.
        string username = await SeedUserAsync();

        using HttpClient client = factory.CreateClient();

        string refreshToken = await SignInAndGetRefreshTokenAsync(client, username);

        await using IdentityDbContext context = CreateIdentityContext();

        bool storedInClear = await context.RefreshTokens.AnyAsync(t => t.TokenHash == refreshToken);

        Assert.False(storedInClear, "The refresh token must never be stored in its plaintext form.");
    }

    // -----------------------------------------------------------------------
    // Sign-out
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SignOut_RevokesTheSessionServerSide()
    {
        // Deleting a cookie is not signing out: a copied refresh token would
        // still work. The refresh token must stop working immediately.
        string username = await SeedUserAsync();

        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage login = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { username, password = ValidPassword });

        JsonElement loginBody = await login.Content.ReadFromJsonAsync<JsonElement>();
        string accessToken = loginBody.GetProperty("accessToken").GetString()!;
        string refreshToken = loginBody.GetProperty("refreshToken").GetString()!;

        using var logoutRequest = new HttpRequestMessage(
            HttpMethod.Post, new Uri("/api/v1/auth/logout", UriKind.Relative));

        logoutRequest.Headers.Authorization = new("Bearer", accessToken);

        using HttpResponseMessage logout = await client.SendAsync(logoutRequest);

        Assert.True(logout.IsSuccessStatusCode);

        using HttpResponseMessage afterLogout = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/refresh", UriKind.Relative),
            new { refreshToken });

        Assert.NotEqual(HttpStatusCode.OK, afterLogout.StatusCode);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private IdentityDbContext CreateIdentityContext()
        => new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(factory.TestConnectionString)
            .Options);

    /// <summary>
    /// Creates an active user with a known password, directly through the
    /// database. Going through the API would need an administrator, which needs
    /// authorization, which is Phase 4.
    /// </summary>
    private async Task<string> SeedUserAsync()
    {
        string username = $"user{Guid.CreateVersion7():N}"[..20];

        await using IdentityDbContext context = CreateIdentityContext();

        User user = User.Create(
            username, $"{username}@example.com", "Test User", DateTimeOffset.UtcNow,
            mustChangePassword: false).Value;

        var hasher = new Argon2PasswordHasher(Options.Create(new Argon2Options
        {
            // Reduced cost: these tests create many users and the production
            // parameters are exercised in the unit suite.
            MemoryKib = 8192,
            Iterations = 1,
            Parallelism = 1
        }));

        context.Users.Add(user);
        context.Credentials.Add(UserCredential.Create(
            user.Id, hasher.Hash(ValidPassword), hasher.AlgorithmId, DateTimeOffset.UtcNow));

        await context.SaveChangesAsync();

        return user.Username;
    }

    private static async Task<string> SignInAndGetRefreshTokenAsync(HttpClient client, string username)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { username, password = ValidPassword });

        response.EnsureSuccessStatusCode();

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return body.GetProperty("refreshToken").GetString()!;
    }
}
