using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CCP.Modules.Identity.Domain.Credentials;
using CCP.Modules.Identity.Domain.Users;
using CCP.Modules.Identity.Infrastructure.Persistence;
using CCP.Modules.Identity.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CCP.Api.IntegrationTests.Authorization;

/// <summary>
/// A signed-in caller can be identified from their own token.
/// <para>
/// <b>This suite exists because that was not true.</b> Three copies of "read the
/// subject from the principal" had grown, and one of them read only <c>sub</c>.
/// ASP.NET Core's JWT handler renames inbound claims by default — <c>sub</c>
/// arrives as a WS-Federation URI — so that copy found nothing on a perfectly
/// valid token and its endpoints answered 401 to everyone.
/// </para>
/// <para>
/// The endpoints it broke were not obscure. Reading one's own permissions is how
/// the portal decides which controls to show, so every administrative button in
/// the product was hidden from the administrator. Granting and revoking a role
/// were the other two, which is to say the Platform could not hand out access at
/// all.
/// </para>
/// <para>
/// None of it was visible from the code: both versions looked correct, both
/// compiled, and the difference only appears at runtime against a real token.
/// </para>
/// </summary>
public sealed class CallerIdentityTests(PlatformApiFactory factory)
    : IClassFixture<PlatformApiFactory>
{
    private const string ValidPassword = "correct-horse-battery-staple";

    [Fact]
    public async Task ReadingOnesOwnPermissions_IdentifiesTheCaller()
    {
        using HttpClient client = factory.CreateClient();
        string token = await SignInAsync(client);

        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri("/api/v1/me/permissions", UriKind.Relative));

        request.Headers.Authorization = new("Bearer", token);

        using HttpResponseMessage response = await client.SendAsync(request);

        // 401 was the answer for every caller, always. The point of this
        // assertion is the status, not the contents: a user with no role holds
        // no permissions, and that is a correct empty answer rather than a
        // refusal to say who is asking.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.TryGetProperty("permissions", out JsonElement permissions));
        Assert.Equal(JsonValueKind.Array, permissions.ValueKind);
    }

    [Fact]
    public async Task GrantingARole_IdentifiesTheGranter()
    {
        using HttpClient client = factory.CreateClient();
        string token = await SignInAsync(client);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/api/v1/users/{Guid.CreateVersion7()}/roles", UriKind.Relative));

        request.Headers.Authorization = new("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            roleId = Guid.CreateVersion7(),
            scope = "All",
            scopeUnitId = (Guid?)null,
            expiresAt = (DateTimeOffset?)null
        });

        using HttpResponseMessage response = await client.SendAsync(request);

        // Whatever this caller is refused for — no permission, no second factor,
        // a role that does not exist — it must not be "I cannot tell who you
        // are". A 401 here means the token was read and the subject was not
        // found in it, which is the defect this suite is about.
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Creates an account and signs it in, returning the access token.</summary>
    private async Task<string> SignInAsync(HttpClient client)
    {
        string username = $"caller{Guid.CreateVersion7():N}"[..20];

        await using var context = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(factory.TestConnectionString)
                .Options);

        User user = User.Create(
            username, $"{username}@example.invalid", "Caller", DateTimeOffset.UtcNow,
            mustChangePassword: false).Value;

        // Reduced cost, like the other suites: these tests are about the token,
        // and the production hashing parameters are exercised in the unit tests.
        var hasher = new Argon2PasswordHasher(Options.Create(new Argon2Options
        {
            MemoryKib = 8192,
            Iterations = 1,
            Parallelism = 1
        }));

        context.Users.Add(user);
        context.Credentials.Add(UserCredential.Create(
            user.Id, hasher.Hash(ValidPassword), hasher.AlgorithmId, DateTimeOffset.UtcNow));

        await context.SaveChangesAsync();

        using HttpResponseMessage login = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { username, password = ValidPassword });

        Assert.True(
            login.IsSuccessStatusCode,
            $"The test account could not sign in: {login.StatusCode}.");

        JsonElement body = await login.Content.ReadFromJsonAsync<JsonElement>();

        return body.GetProperty("accessToken").GetString()!;
    }
}
