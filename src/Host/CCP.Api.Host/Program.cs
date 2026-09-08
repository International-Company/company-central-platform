using System.Globalization;
using System.Threading.RateLimiting;
using CCP.Api.Host.Configuration;
using CCP.Api.Host.Modules;
using CCP.Kernel.Api.Context;
using CCP.Kernel.Api.Errors;
using CCP.Kernel.Api.Security;
using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Application.Events;
using CCP.Kernel.Infrastructure.Outbox;
using CCP.Kernel.Infrastructure.Persistence;
using CCP.Kernel.Primitives;
using CCP.Modules.Identity.Application;
using CCP.Modules.Identity.Infrastructure;
using CCP.Modules.Identity.Infrastructure.Security;
using CCP.Modules.Authorization.Infrastructure;
using CCP.Modules.Authorization.Infrastructure.Seeding;
using CCP.Modules.Organization.Infrastructure;
using CCP.Modules.Security.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Serilog;

// ============================================================================
// Composition root.
//
// This file contains no logic — only configuration, module registration and
// middleware ordering (ARCHITECTURE.md §8.2). Anything that looks like
// behaviour belongs in the kernel or in a module.
// ============================================================================

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------
// The CCP_ prefix keeps Platform settings distinct from unrelated environment
// variables, and is how secrets arrive from the cloud secret manager in
// staging and production (ARCHITECTURE.md §12.7).
builder.Configuration.AddEnvironmentVariables(prefix: "CCP_");

builder.Services
    .AddOptions<PlatformOptions>()
    .Bind(builder.Configuration.GetSection(PlatformOptions.SectionName))
    .ValidateDataAnnotations()
    // Validate at startup, not on first use: a misconfigured deployment should
    // fail immediately and visibly rather than at an arbitrary later request.
    .ValidateOnStart();

builder.Services
    .AddOptions<OutboxOptions>()
    .Bind(builder.Configuration.GetSection(OutboxOptions.SectionName))
    .ValidateOnStart();

// ---------------------------------------------------------------------------
// Logging
// ---------------------------------------------------------------------------
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

// ---------------------------------------------------------------------------
// Kernel services
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<IClock, SystemClock>();

builder.Services.AddScoped<RequestContextAccessor>();
builder.Services.AddScoped<IRequestContext>(sp => sp.GetRequiredService<RequestContextAccessor>());

builder.Services.AddScoped<IOutbox, OutboxWriter>();
builder.Services.AddScoped<IIntegrationEventDispatcher, IntegrationEventDispatcher>();
builder.Services.AddHostedService<OutboxRelay>();

string connectionString = builder.Configuration.GetConnectionString("Platform")
    ?? throw new InvalidOperationException(
        "Connection string 'Platform' is not configured. Set CCP_ConnectionStrings__Platform. "
        + "See .env.example.");

builder.Services.AddDbContext<KernelDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__ef_migrations_history", KernelDbContext.SchemaName)));

// ---------------------------------------------------------------------------
// API surface
// ---------------------------------------------------------------------------
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<KernelDbContext>(
        name: "database",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"]);

// Rate limiting (ARCHITECTURE.md §12.6). Named policies per endpoint class,
// applied by the endpoints themselves — a single global limit generous enough
// for browsing is enormous for password guessing, and the two cannot share one
// number.
//
// The global limiter remains as a backstop for anything that declares no policy,
// so an endpoint added without one is still bounded.
builder.Services.AddRateLimiter(options =>
{
    options.AddPlatformPolicies();

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.FindFirst("sub")?.Value
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 600,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// CORS: a strict origin allow-list. Wildcards are prohibited
// (ARCHITECTURE.md §12.9); an empty list means no cross-origin access at all.
string[] allowedOrigins = builder.Configuration
    .GetSection("Platform:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()
        .WithExposedHeaders(
            CorrelationIdMiddleware.CorrelationHeader,
            CorrelationIdMiddleware.RequestHeader)));

// Bearer authentication. The signing key comes from the Identity module's
// key provider, so the API validates exactly the tokens it issued, against the
// same key that is published via JWKS for business applications (ADR-006).
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(jwt =>
    {
        IdentityOptions identityOptions = builder.Configuration
            .GetSection(IdentityOptions.SectionName)
            .Get<IdentityOptions>() ?? new IdentityOptions();

        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = identityOptions.Issuer,
            ValidAudience = identityOptions.Audience,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            // No skew allowance. The default five minutes keeps an expired token
            // working for five minutes past its expiry — and expiry is the only
            // revocation an access token has.
            ClockSkew = TimeSpan.Zero
        };

    });

// The signing key is supplied through DI rather than read inline, because the
// key provider is a singleton built by this same container and is not available
// while the options delegate above is being constructed.
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<ISigningKeyProvider>((jwt, keyProvider) =>
        jwt.TokenValidationParameters.IssuerSigningKey =
            new RsaSecurityKey(keyProvider.GetSigningKey()) { KeyId = keyProvider.KeyId });

builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------
// Modules
// ---------------------------------------------------------------------------
// Module infrastructure is registered by the host, the only place permitted to
// know about an Api layer and an Infrastructure layer at once (§6.1).
builder.Services.AddIdentityInfrastructure(builder.Configuration, connectionString);
builder.Services.AddOrganizationInfrastructure(builder.Configuration, connectionString);
builder.Services.AddAuthorizationInfrastructure(builder.Configuration, connectionString);
builder.Services.AddSecurityInfrastructure(builder.Configuration, connectionString);

builder.Services.AddPlatformModules(builder.Configuration);

WebApplication app = builder.Build();

// ===========================================================================
// Middleware pipeline — order matters (ARCHITECTURE.md §8.3).
//
// Correlation ids are established before anything that can fail, so every
// error is traceable. The exception boundary sits outside everything it must
// catch.
// ===========================================================================

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseSerilogRequestLogging(options =>
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        var requestContext = httpContext.RequestServices.GetRequiredService<RequestContextAccessor>();
        diagnosticContext.Set("CorrelationId", requestContext.CorrelationId);
    });

app.UseRateLimiter();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// ---------------------------------------------------------------------------
// Health checks
// ---------------------------------------------------------------------------
// Liveness is anonymous and returns no internal detail — it tells an attacker
// nothing beyond "the process is running" (ARCHITECTURE.md §22.4).
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();

// Readiness checks dependencies. The bare status is public so the load
// balancer can route; the detail is not.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

// ---------------------------------------------------------------------------
// API endpoints
// ---------------------------------------------------------------------------
// Version in the URL path (ADR-008), so it is visible in every log line,
// bug report and curl command.
RouteGroupBuilder v1 = app.MapGroup("/api/v1");
v1.MapPlatformModules();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// ---------------------------------------------------------------------------
// Authorization seeding
// ---------------------------------------------------------------------------
// Runs after endpoints are mapped, because the Platform's permission list is
// derived from their metadata rather than a hand-maintained manifest — the two
// can then never drift.
//
// Failure is logged, not fatal: a Platform that cannot reach the database at
// startup should still come up and report itself unhealthy, rather than
// crash-looping and hiding the reason.
try
{
    var seeder = app.Services.GetRequiredService<AuthorizationSeeder>();
    var endpointSource = app.Services.GetRequiredService<EndpointDataSource>();

    await seeder.SeedAsync(endpointSource);
}
catch (Exception exception)
{
    app.Logger.LogError(
        exception,
        "Authorization seeding failed. Permissions may be missing until this is resolved.");
}

await app.RunAsync();

/// <summary>
/// Exposed so the integration tests can construct the host through
/// <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program;
