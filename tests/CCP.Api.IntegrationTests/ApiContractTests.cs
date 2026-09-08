using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CCP.Kernel.Api.Context;

namespace CCP.Api.IntegrationTests;

/// <summary>
/// Verifies the cross-cutting API contract every module will inherit: the error
/// shape, correlation ids, security headers, paging rules and the error
/// boundary (ADR-008, ARCHITECTURE.md §8.3).
/// <para>
/// These run against the real host and a real database. Getting them right now
/// means eleven modules inherit a contract that is already proven.
/// </para>
/// </summary>
public sealed class ApiContractTests(PlatformApiFactory factory) : IClassFixture<PlatformApiFactory>
{
    private HttpClient CreateClient() => factory.CreateClient();

    // -----------------------------------------------------------------------
    // The vertical slice
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Ping_ReturnsOk()
    {
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/diagnostics/ping", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("ok", body.GetProperty("status").GetString());
    }

    // -----------------------------------------------------------------------
    // Correlation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Response_CarriesGeneratedCorrelationId_WhenNoneSupplied()
    {
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/diagnostics/ping", UriKind.Relative));

        Assert.True(response.Headers.Contains(CorrelationIdMiddleware.CorrelationHeader));

        string correlationId = response.Headers
            .GetValues(CorrelationIdMiddleware.CorrelationHeader)
            .Single();

        Assert.False(string.IsNullOrWhiteSpace(correlationId));

        // The body reports the same id the header does, so a user quoting what
        // they saw gives an engineer something that matches the logs.
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(correlationId, body.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Response_EchoesInboundCorrelationId()
    {
        using HttpClient client = CreateClient();

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri("/api/v1/diagnostics/ping", UriKind.Relative));

        request.Headers.Add(CorrelationIdMiddleware.CorrelationHeader, "caller-supplied-123");

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(
            "caller-supplied-123",
            response.Headers.GetValues(CorrelationIdMiddleware.CorrelationHeader).Single());
    }

    [Fact]
    public async Task CorrelationId_IsSanitised_WhenCallerSuppliesUnsafeCharacters()
    {
        using HttpClient client = CreateClient();

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri("/api/v1/diagnostics/ping", UriKind.Relative));

        // A correlation id is attacker-controlled and reaches log files, so
        // anything that could forge a log line must be stripped.
        request.Headers.TryAddWithoutValidation(
            CorrelationIdMiddleware.CorrelationHeader,
            "abc\u0000def<script>alert(1)</script>");

        using HttpResponseMessage response = await client.SendAsync(request);

        string returned = response.Headers
            .GetValues(CorrelationIdMiddleware.CorrelationHeader)
            .Single();

        Assert.DoesNotContain("<", returned, StringComparison.Ordinal);
        Assert.DoesNotContain(">", returned, StringComparison.Ordinal);
        Assert.DoesNotContain("(", returned, StringComparison.Ordinal);
        Assert.Matches("^[A-Za-z0-9._-]+$", returned);
    }

    // -----------------------------------------------------------------------
    // Security headers
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("X-Content-Type-Options", "nosniff")]
    [InlineData("X-Frame-Options", "DENY")]
    [InlineData("Referrer-Policy", "strict-origin-when-cross-origin")]
    public async Task Response_CarriesSecurityHeaders(string header, string expectedValue)
    {
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/diagnostics/ping", UriKind.Relative));

        Assert.True(response.Headers.Contains(header), $"Missing header: {header}");
        Assert.Equal(expectedValue, response.Headers.GetValues(header).Single());
    }

    [Fact]
    public async Task Response_CarriesContentSecurityPolicy()
    {
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/diagnostics/ping", UriKind.Relative));

        string csp = response.Headers.GetValues("Content-Security-Policy").Single();

        Assert.Contains("default-src 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/v1/diagnostics/throw")]
    [InlineData("/api/v1/diagnostics/error/notfound")]
    [InlineData("/api/v1/diagnostics/error/validation")]
    public async Task ErrorResponses_AlsoCarrySecurityHeaders(string path)
    {
        // Regression test for a defect found in Phase 1: the exception boundary
        // called Response.Clear() before writing Problem Details, which stripped
        // the security headers set earlier in the pipeline. Error responses were
        // therefore served without CSP, nosniff or frame protection.
        //
        // An error response is exactly the kind an attacker provokes deliberately,
        // so it must be no less protected than a successful one.
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.False(response.IsSuccessStatusCode, "This test must exercise a failure response.");

        Assert.True(
            response.Headers.Contains("X-Content-Type-Options"),
            $"{path} returned an error without X-Content-Type-Options.");
        Assert.True(
            response.Headers.Contains("X-Frame-Options"),
            $"{path} returned an error without X-Frame-Options.");
        Assert.True(
            response.Headers.Contains("Content-Security-Policy"),
            $"{path} returned an error without a Content-Security-Policy.");
        Assert.True(
            response.Headers.Contains("Referrer-Policy"),
            $"{path} returned an error without a Referrer-Policy.");
    }

    // -----------------------------------------------------------------------
    // The error contract (RFC 9457)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("validation", HttpStatusCode.UnprocessableEntity)]
    [InlineData("notfound", HttpStatusCode.NotFound)]
    [InlineData("conflict", HttpStatusCode.Conflict)]
    [InlineData("forbidden", HttpStatusCode.Forbidden)]
    public async Task ErrorResponses_UseTheCorrectStatusCode(string kind, HttpStatusCode expected)
    {
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri($"/api/v1/diagnostics/error/{kind}", UriKind.Relative));

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ErrorResponse_ContainsTheRequiredFields()
    {
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/diagnostics/error/notfound", UriKind.Relative));

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Every field a client is entitled to rely on.
        Assert.True(body.TryGetProperty("type", out _));
        Assert.True(body.TryGetProperty("title", out _));
        Assert.True(body.TryGetProperty("status", out _));
        Assert.True(body.TryGetProperty("detail", out _));
        Assert.True(body.TryGetProperty("code", out _));
        Assert.True(body.TryGetProperty("correlationId", out _));

        Assert.Equal("PLATFORM.RESOURCE_NOT_FOUND", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ValidationError_ListsEveryFieldError()
    {
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/diagnostics/error/validation", UriKind.Relative));

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement errors = body.GetProperty("errors");

        Assert.Equal(2, errors.GetArrayLength());
        Assert.Equal("name", errors[0].GetProperty("field").GetString());
        Assert.Equal("PLATFORM.FIELD_REQUIRED", errors[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task NonValidationError_OmitsFieldErrors()
    {
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/diagnostics/error/forbidden", UriKind.Relative));

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(body.TryGetProperty("errors", out _));
    }

    // -----------------------------------------------------------------------
    // The error boundary
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UnhandledException_ReturnsProblemDetailsAndLeaksNothing()
    {
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/diagnostics/throw", UriKind.Relative));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        string raw = await response.Content.ReadAsStringAsync();

        // The response must reveal nothing about the internals: no exception
        // type, no message, no stack frame, no file path (ARCHITECTURE.md §11.3).
        Assert.DoesNotContain("InvalidOperationException", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Deliberate failure", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CCP.Api.Host", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", raw, StringComparison.Ordinal);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("PLATFORM.INTERNAL_ERROR", body.GetProperty("code").GetString());

        // Still traceable, which is the whole point of the correlation id.
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("correlationId").GetString()));
    }

    // -----------------------------------------------------------------------
    // Paging
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PagedEndpoint_ReturnsTheStandardEnvelope()
    {
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/diagnostics/paged?page=2&pageSize=10", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2, body.GetProperty("page").GetInt32());
        Assert.Equal(10, body.GetProperty("pageSize").GetInt32());
        Assert.Equal(137, body.GetProperty("totalItems").GetInt64());
        Assert.Equal(14, body.GetProperty("totalPages").GetInt32());
        Assert.Equal(10, body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task PagedEndpoint_RejectsPageSizeAboveTheCap()
    {
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/diagnostics/paged?pageSize=5000", UriKind.Relative));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("PLATFORM.INVALID_PAGE_SIZE", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PagedEndpoint_RejectsSortFieldOutsideTheAllowList()
    {
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/diagnostics/paged?sort=passwordHash", UriKind.Relative));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("PLATFORM.SORT_FIELD_NOT_ALLOWED", body.GetProperty("code").GetString());
    }

    // -----------------------------------------------------------------------
    // Health
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LivenessProbe_IsAnonymousAndRevealsNoDetail()
    {
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();

        // A bare status. It must not enumerate dependencies to an anonymous
        // caller (ARCHITECTURE.md §22.4).
        Assert.Equal("Healthy", body);
        Assert.DoesNotContain("database", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadinessProbe_ReportsHealthyWhenTheDatabaseIsReachable()
    {
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
