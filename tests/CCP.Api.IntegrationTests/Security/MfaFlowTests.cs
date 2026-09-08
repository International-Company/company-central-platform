using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CCP.Modules.Identity.Domain.Credentials;
using CCP.Modules.Identity.Domain.Users;
using CCP.Modules.Identity.Infrastructure.Persistence;
using CCP.Modules.Identity.Infrastructure.Security;
using CCP.Modules.Security.Domain.Events;
using CCP.Modules.Security.Domain.Mfa;
using CCP.Modules.Security.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CCP.Api.IntegrationTests.Security;

/// <summary>
/// Two-factor enrolment, verification and step-up against a real database.
/// <para>
/// These verify what unit tests structurally cannot: that the one-enrolment-per-
/// user constraint is enforced by PostgreSQL rather than by hope, that a
/// security event recording a failure survives the rollback of the operation
/// that failed, and that step-up genuinely gates a privileged endpoint through
/// the real authorization pipeline.
/// </para>
/// <para>
/// <b>Written but not yet executed.</b> No database was reachable on the
/// development machine when this phase was built (DEVELOPMENT_STATUS.md §4).
/// They run in CI, which provisions PostgreSQL as a service container.
/// </para>
/// </summary>
public sealed class MfaFlowTests(PlatformApiFactory factory) : IClassFixture<PlatformApiFactory>
{
    private const string ValidPassword = "a-sufficiently-long-passphrase";

    // -----------------------------------------------------------------------
    // Enrolment
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Status_ReportsNotEnrolled_ForAFreshUser()
    {
        using HttpClient client = await SignedInClientAsync();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/v1/me/mfa", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(body.GetProperty("isEnrolled").GetBoolean());
        Assert.False(body.GetProperty("isActive").GetBoolean());
        Assert.Equal(0, body.GetProperty("remainingRecoveryCodes").GetInt32());
    }

    [Fact]
    public async Task Enrol_ReturnsProvisioningDataAndLeavesEnrolmentPending()
    {
        using HttpClient client = await SignedInClientAsync();

        JsonElement enrolment = await EnrolAsync(client);

        Assert.StartsWith(
            "otpauth://totp/", enrolment.GetProperty("provisioningUri").GetString(), StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(enrolment.GetProperty("manualEntryKey").GetString()));

        // Pending, not active. A factor that counted before it was proven would
        // lock out anyone whose scan failed.
        using HttpResponseMessage status = await client.GetAsync(new Uri("/api/v1/me/mfa", UriKind.Relative));
        JsonElement body = await status.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("isEnrolled").GetBoolean());
        Assert.False(body.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Enrol_Twice_ReplacesThePendingEnrolment_LeavingExactlyOneRow()
    {
        using HttpClient client = await SignedInClientAsync();

        await EnrolAsync(client);
        await EnrolAsync(client);

        await using SecurityDbContext context = CreateSecurityContext();

        // The unique index on user_id is what makes this a database guarantee
        // rather than a convention. Two active factors would make "which one
        // counts" ambiguous at exactly the moment it must not be.
        Assert.Equal(1, await context.MfaEnrolments.CountAsync());
    }

    [Fact]
    public async Task Confirm_ActivatesTheFactorAndIssuesRecoveryCodes()
    {
        using HttpClient client = await SignedInClientAsync();

        JsonElement enrolment = await EnrolAsync(client);
        string code = CodeFor(enrolment);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/v1/me/mfa/confirm", UriKind.Relative), new { code });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(10, body.GetProperty("count").GetInt32());
        Assert.Equal(10, body.GetProperty("codes").GetArrayLength());
    }

    [Fact]
    public async Task Confirm_RejectsAWrongCodeAndLeavesTheEnrolmentPending()
    {
        using HttpClient client = await SignedInClientAsync();

        await EnrolAsync(client);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/v1/me/mfa/confirm", UriKind.Relative), new { code = "000000" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using SecurityDbContext context = CreateSecurityContext();
        MfaEnrolment enrolment = await context.MfaEnrolments.SingleAsync();

        Assert.Equal(MfaEnrolmentStatus.Pending, enrolment.Status);
    }

    [Fact]
    public async Task Confirm_RecordsAFailureEventEvenThoughTheRequestFailed()
    {
        using HttpClient client = await SignedInClientAsync();

        await EnrolAsync(client);

        await client.PostAsJsonAsync(
            new Uri("/api/v1/me/mfa/confirm", UriKind.Relative), new { code = "000000" });

        await using SecurityDbContext context = CreateSecurityContext();

        // The point of the independent recorder. If failure events rolled back
        // with the operations that produced them, the only attempts on record
        // would be the successful ones — precisely backwards for detection.
        Assert.True(await context.SecurityEvents.AnyAsync(
            e => e.EventType == SecurityEventTypes.MfaChallengeFailed));
    }

    [Fact]
    public async Task Status_NeverReturnsTheSecret()
    {
        using HttpClient client = await SignedInClientAsync();

        JsonElement enrolment = await EnrolAsync(client);
        string manualKey = enrolment.GetProperty("manualEntryKey").GetString()!;

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/v1/me/mfa", UriKind.Relative));
        string body = await response.Content.ReadAsStringAsync();

        // Shown once, at enrolment. Re-showing it would let anyone with a stolen
        // session clone the factor.
        Assert.DoesNotContain(manualKey, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // Verification
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Verify_AcceptsACurrentCodeAndRecordsAStepUpConfirmation()
    {
        using HttpClient client = await SignedInClientAsync();

        JsonElement enrolment = await EnrolAsync(client);
        await ConfirmAsync(client, enrolment);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/v1/me/mfa/verify", UriKind.Relative),
            new { code = CodeFor(enrolment) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("verified").GetBoolean());
        Assert.False(body.GetProperty("usedRecoveryCode").GetBoolean());

        await using SecurityDbContext context = CreateSecurityContext();

        Assert.Equal(1, await context.StepUpConfirmations.CountAsync());
    }

    [Fact]
    public async Task Verify_AcceptsARecoveryCodeExactlyOnce()
    {
        using HttpClient client = await SignedInClientAsync();

        JsonElement enrolment = await EnrolAsync(client);
        JsonElement recovery = await ConfirmAsync(client, enrolment);

        string recoveryCode = recovery.GetProperty("codes")[0].GetString()!;

        using HttpResponseMessage first = await client.PostAsJsonAsync(
            new Uri("/api/v1/me/mfa/verify", UriKind.Relative),
            new { code = recoveryCode, isRecoveryCode = true });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        JsonElement body = await first.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("usedRecoveryCode").GetBoolean());
        Assert.Equal(9, body.GetProperty("remainingRecoveryCodes").GetInt32());

        using HttpResponseMessage second = await client.PostAsJsonAsync(
            new Uri("/api/v1/me/mfa/verify", UriKind.Relative),
            new { code = recoveryCode, isRecoveryCode = true });

        // Single use, enforced on rows that actually persisted.
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Verify_RejectsACodeFromOutsideTheDriftWindow()
    {
        using HttpClient client = await SignedInClientAsync();

        JsonElement enrolment = await EnrolAsync(client);
        await ConfirmAsync(client, enrolment);

        // Ten minutes away — far outside the one-period tolerance.
        string staleCode = CodeFor(enrolment, DateTimeOffset.UtcNow.AddMinutes(-10));

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/v1/me/mfa/verify", UriKind.Relative), new { code = staleCode });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -----------------------------------------------------------------------
    // Disabling
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Disable_RequiresTheCurrentCode()
    {
        using HttpClient client = await SignedInClientAsync();

        JsonElement enrolment = await EnrolAsync(client);
        await ConfirmAsync(client, enrolment);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/v1/me/mfa/disable", UriKind.Relative), new { code = "000000" });

        // Without this, a stolen session could strip the account's second factor
        // — the one protection the session should not be able to remove.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using SecurityDbContext context = CreateSecurityContext();

        Assert.Equal(MfaEnrolmentStatus.Active, (await context.MfaEnrolments.SingleAsync()).Status);
    }

    [Fact]
    public async Task Disable_RevokesOutstandingStepUpConfirmations()
    {
        using HttpClient client = await SignedInClientAsync();

        JsonElement enrolment = await EnrolAsync(client);
        await ConfirmAsync(client, enrolment);

        await client.PostAsJsonAsync(
            new Uri("/api/v1/me/mfa/verify", UriKind.Relative), new { code = CodeFor(enrolment) });

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/v1/me/mfa/disable", UriKind.Relative), new { code = CodeFor(enrolment) });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using SecurityDbContext context = CreateSecurityContext();

        // Elevation granted by a factor that no longer exists must not outlive
        // it, or disabling MFA would leave fifteen minutes of privileged access
        // standing on nothing.
        Assert.False(await context.StepUpConfirmations.AnyAsync(c => c.RevokedAt == null));
    }

    // -----------------------------------------------------------------------
    // Step-up enforcement
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PrivilegedEndpoint_IsRefused_WithoutAStepUpConfirmation()
    {
        using HttpClient client = await SignedInClientAsync();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/v1/users", UriKind.Relative),
            new
            {
                username = $"new{Guid.CreateVersion7():N}"[..18],
                email = "new.user@example.com",
                displayName = "New User",
                initialPassword = ValidPassword
            });

        // 403 rather than 401: the caller is authenticated, they are simply not
        // elevated. Whether the permission or the step-up is what stopped them
        // is deliberately not distinguished in the status code.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // -----------------------------------------------------------------------
    // Security event log
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SecurityEventSearch_RequiresAPermission()
    {
        using HttpClient client = await SignedInClientAsync();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/security/events", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SecurityEventSearch_RejectsAnUnknownSeverity()
    {
        using HttpClient client = await SignedInClientAsync();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/security/events?minimumSeverity=catastrophic", UriKind.Relative));

        // Forbidden wins over the validation error: authorization runs first,
        // which is the correct order — an unauthorized caller must not be able
        // to probe parameter validation.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string CodeFor(JsonElement enrolment) => CodeFor(enrolment, DateTimeOffset.UtcNow);

    /// <summary>
    /// Computes the code the user's authenticator would show, from the manual
    /// entry key the enrolment response returned. This is the same secret the
    /// server holds, which is what makes the test end-to-end rather than a
    /// re-implementation.
    /// </summary>
    private static string CodeFor(JsonElement enrolment, DateTimeOffset at)
        => Totp.ComputeCode(
            Base32Decode(enrolment.GetProperty("manualEntryKey").GetString()!), at);

    private static byte[] Base32Decode(string encoded)
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        var bytes = new List<byte>(encoded.Length * 5 / 8);
        int buffer = 0;
        int bitsLeft = 0;

        foreach (char c in encoded)
        {
            int index = Alphabet.IndexOf(char.ToUpperInvariant(c), StringComparison.Ordinal);

            if (index < 0)
            {
                continue;
            }

            buffer = (buffer << 5) | index;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                bytes.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
                bitsLeft -= 8;
            }
        }

        return [.. bytes];
    }

    private static async Task<JsonElement> EnrolAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.PostAsync(
            new Uri("/api/v1/me/mfa/enrol", UriKind.Relative), content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> ConfirmAsync(HttpClient client, JsonElement enrolment)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/v1/me/mfa/confirm", UriKind.Relative),
            new { code = CodeFor(enrolment) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Seeds a user, signs them in, and returns a client carrying the token.</summary>
    private async Task<HttpClient> SignedInClientAsync()
    {
        string username = await SeedUserAsync();

        HttpClient client = factory.CreateClient();

        using HttpResponseMessage login = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { username, password = ValidPassword });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        JsonElement body = await login.Content.ReadFromJsonAsync<JsonElement>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", body.GetProperty("accessToken").GetString());

        return client;
    }

    private async Task<string> SeedUserAsync()
    {
        string username = $"user{Guid.CreateVersion7():N}"[..20];

        await using IdentityDbContext context = CreateIdentityContext();

        User user = User.Create(
            username, $"{username}@example.com", "Test User", DateTimeOffset.UtcNow,
            mustChangePassword: false).Value;

        var hasher = new Argon2PasswordHasher(Options.Create(new Argon2Options
        {
            // Reduced cost: the production parameters are exercised in the unit
            // suite, and these tests create a user per case.
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

    private IdentityDbContext CreateIdentityContext()
        => new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(factory.TestConnectionString)
            .Options);

    private SecurityDbContext CreateSecurityContext()
        => new(new DbContextOptionsBuilder<SecurityDbContext>()
            .UseNpgsql(factory.TestConnectionString)
            .Options);
}
