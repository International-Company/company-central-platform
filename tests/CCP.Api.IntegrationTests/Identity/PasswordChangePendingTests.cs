using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CCP.Modules.Identity.Domain.Credentials;
using CCP.Modules.Identity.Domain.Users;
using CCP.Modules.Identity.Infrastructure.Persistence;
using CCP.Modules.Identity.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CCP.Api.IntegrationTests.Identity;

/// <summary>
/// A temporary password buys exactly one thing: the chance to replace it.
/// <para>
/// <b>The portal enforced this and nothing else did.</b> It reads
/// <c>mustChangePassword</c> from <c>/me</c> and redirects to the change screen,
/// which covers everybody who uses a browser and covers nobody who calls the API
/// directly. So a password an administrator issued — a password one other person
/// knows — was a working credential for the entire API until its holder happened
/// to open the portal. A forced change exists precisely so that window is short.
/// </para>
/// <para>
/// Found while wiring the end-to-end seed, which had to sign in before the
/// change and discovered it could create companies, units, employees, users and
/// roles.
/// </para>
/// </summary>
public sealed class PasswordChangePendingTests(PlatformApiFactory factory)
    : IClassFixture<PlatformApiFactory>
{
    private const string TemporaryPassword = "issued-by-an-administrator-2026";
    private const string ChosenPassword = "correct-horse-battery-staple";

    [Fact]
    public async Task ACallerWhoOwesAPasswordChangeIsRefusedEverythingElse()
    {
        using HttpClient client = await SignedInAsync(mustChangePassword: true);

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/users?page=1&pageSize=1", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // The machine code, not the sentence. A client seeing this knows to send
        // the person to the change-password flow rather than to re-authenticate
        // or to ask somebody for a permission — which are the two things the
        // other 403s in the Platform mean.
        Assert.Equal(
            "IDENTITY.PASSWORD_CHANGE_REQUIRED",
            body.GetProperty("code").GetString());
    }

    /// <summary>
    /// 403 and not 401. The credential is valid and was understood; what is
    /// refused is everything else until the one thing owed is done. A 401 would
    /// send a client back to re-authenticate, which succeeds and changes
    /// nothing — an infinite and very confusing loop.
    /// </summary>
    [Fact]
    public async Task TheRefusalIsNotAnInvitationToSignInAgain()
    {
        using HttpClient client = await SignedInAsync(mustChangePassword: true);

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/roles", UriKind.Relative));

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// The one endpoint that must stay open. Refusing it would leave somebody
    /// holding a temporary password with no way to stop holding it.
    /// </summary>
    [Fact]
    public async Task ChangingThePasswordItselfIsStillReachable()
    {
        using HttpClient client = await SignedInAsync(mustChangePassword: true);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/password/change", UriKind.Relative),
            new { currentPassword = TemporaryPassword, newPassword = ChosenPassword });

        Assert.True(
            response.IsSuccessStatusCode,
            $"The change was refused with {response.StatusCode}, which leaves the caller stuck.");
    }

    /// <summary>
    /// And so must reading one's own profile: it is how a client discovers the
    /// obligation exists at all. Refusing it would leave the portal unable to
    /// explain why everything else is being refused.
    /// </summary>
    [Fact]
    public async Task ReadingOnesOwnProfileIsStillReachable()
    {
        using HttpClient client = await SignedInAsync(mustChangePassword: true);

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("mustChangePassword").GetBoolean());
    }

    /// <summary>
    /// Somebody who does not want to change their password right now must still
    /// be able to leave. A session nobody can end is worse than one that can do
    /// nothing.
    /// </summary>
    [Fact]
    public async Task SigningOutIsStillReachable()
    {
        using HttpClient client = await SignedInAsync(mustChangePassword: true);

        using HttpResponseMessage response = await client.PostAsync(
            new Uri("/api/v1/auth/logout", UriKind.Relative), content: null);

        Assert.True(
            response.IsSuccessStatusCode,
            $"Signing out was refused with {response.StatusCode}.");
    }

    /// <summary>
    /// An account that owes nothing is unaffected, which is nearly everybody.
    /// Without this the suite would pass on a build that refused all callers.
    /// </summary>
    [Fact]
    public async Task ACallerWhoOwesNothingIsUnaffected()
    {
        using HttpClient client = await SignedInAsync(mustChangePassword: false);

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // 403 here would be the permission check, which is a different refusal
        // and a correct one for an account holding no roles. What must not
        // appear is the password-change code.
        using HttpResponseMessage roles = await client.GetAsync(
            new Uri("/api/v1/roles", UriKind.Relative));

        if (roles.StatusCode == HttpStatusCode.Forbidden)
        {
            JsonElement body = await roles.Content.ReadFromJsonAsync<JsonElement>();

            Assert.NotEqual(
                "IDENTITY.PASSWORD_CHANGE_REQUIRED",
                body.GetProperty("code").GetString());
        }
    }

    // --- Fixtures -----------------------------------------------------------

    private async Task<HttpClient> SignedInAsync(bool mustChangePassword)
    {
        // Version 4. The leading hex of a UUIDv7 is a millisecond timestamp, so
        // a truncated one collides between tests in the same instant.
        string username = $"pwd{Guid.NewGuid():N}"[..20];

        await using var identity = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(factory.TestConnectionString)
                .Options);

        User user = User.Create(
            username, $"{username}@example.invalid", "Owes A Change",
            DateTimeOffset.UtcNow, mustChangePassword).Value;

        var hasher = new Argon2PasswordHasher(Options.Create(new Argon2Options
        {
            MemoryKib = 8192,
            Iterations = 1,
            Parallelism = 1
        }));

        identity.Users.Add(user);
        identity.Credentials.Add(UserCredential.Create(
            user.Id, hasher.Hash(TemporaryPassword), hasher.AlgorithmId, DateTimeOffset.UtcNow));

        await identity.SaveChangesAsync();

        HttpClient client = factory.CreateClient();

        using HttpResponseMessage login = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { username, password = TemporaryPassword });

        Assert.True(login.IsSuccessStatusCode, $"Sign-in failed: {login.StatusCode}.");

        JsonElement body = await login.Content.ReadFromJsonAsync<JsonElement>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", body.GetProperty("accessToken").GetString());

        return client;
    }
}
