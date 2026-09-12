using System.Globalization;
using System.Threading.RateLimiting;
using CCP.Api.Host.Configuration;
using CCP.Api.Host.Modules;
using CCP.Kernel.Api.Context;
using CCP.Kernel.Api.Errors;
using CCP.Kernel.Api.Observability;
using CCP.Kernel.Api.Security;
using CCP.Kernel.Api.Versioning;
using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Application.Auditing;
using CCP.Kernel.Application.Security;
using CCP.Kernel.Application.Configuration;
using CCP.Kernel.Application.Events;
using CCP.Kernel.Application.Jobs;
using CCP.Kernel.Application.Observability;
using CCP.Kernel.Infrastructure.Jobs;
using CCP.Kernel.Infrastructure.Outbox;
using CCP.Kernel.Infrastructure.Persistence;
using CCP.Kernel.Primitives;
using Microsoft.AspNetCore.Authorization;
using CCP.Modules.Identity.Application;
using CCP.Modules.Identity.Infrastructure;
using CCP.Modules.Identity.Infrastructure.Bootstrap;
using CCP.Modules.Identity.Infrastructure.Persistence;
using CCP.Modules.Identity.Infrastructure.Security;
using CCP.Modules.Audit.Infrastructure;
using CCP.Modules.Workflow.Infrastructure;
using CCP.Modules.Documents.Application;
using CCP.Modules.Documents.Infrastructure;
using CCP.Modules.Configuration.Infrastructure;
using CCP.Modules.Configuration.Infrastructure.Persistence;
using CCP.Modules.Integrations.Application;
using CCP.Modules.Integrations.Infrastructure;
using CCP.Modules.Integrations.Infrastructure.Persistence;
using CCP.Modules.Documents.Infrastructure.Persistence;
using CCP.Modules.Notifications.Infrastructure;
using CCP.Modules.Notifications.Infrastructure.Persistence;
using CCP.Modules.Workflow.Infrastructure.Persistence;
using CCP.Modules.Audit.Infrastructure.Persistence;
using CCP.Modules.Authorization.Infrastructure;
using CCP.Modules.Authorization.Infrastructure.Persistence;
using CCP.Modules.Authorization.Infrastructure.Seeding;
using CCP.Modules.Organization.Infrastructure;
using CCP.Modules.Organization.Infrastructure.Persistence;
using CCP.Modules.Security.Infrastructure;
using CCP.Modules.Security.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
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

    // Credentials are removed on the way out, not left to the discipline of
    // whoever writes each log statement (ARCHITECTURE.md §22.2). Every leak of
    // this kind is written by somebody being careful who did not know the object
    // they logged carried a token three properties down.
    .Enrich.With<LogRedactionEnricher>()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

// ---------------------------------------------------------------------------
// Kernel services
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<IClock, SystemClock>();

builder.Services.AddScoped<RequestContextAccessor>();
builder.Services.AddScoped<IRequestContext>(sp => sp.GetRequiredService<RequestContextAccessor>());

// Declared in Phase 1 and unimplemented until now: audit needs an actor to
// attribute events to, and it must come from the validated token rather than
// from anything a caller could assert.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// The default trail records nothing, so a host without the Audit module still
// starts. The Audit module replaces it; registration order makes that work,
// since the last registration of a service type wins.
builder.Services.AddScoped<IAuditTrail, NullAuditTrail>();

// Whether disabling an account would leave nobody able to grant a role.
// Identity asks; Authorization answers; neither references the other. The
// default here says no, because a host without Authorization has no grants to
// be the last of.
builder.Services.AddScoped<IAdministratorSafety, NoGrantsToStrand>();

// Settings a module may read without knowing the Configuration module exists.
// The default answers every caller with the value it shipped with, so a host
// without Configuration behaves exactly as the Platform did before settings
// were changeable. Configuration replaces it; the last registration wins.
builder.Services.AddSingleton<IPlatformSettings, DefaultPlatformSettings>();

// Background jobs record what they did, so "did last night's purge run?" is a
// page somebody opens rather than a query somebody writes during the incident.
// The journal is a singleton with its own scope per write, because the jobs
// that use it are singletons and because the record of a failed pass must not
// roll back with the pass.
builder.Services
    .AddOptions<JobJournalOptions>()
    .Bind(builder.Configuration.GetSection(JobJournalOptions.SectionName));

builder.Services.AddSingleton<IJobJournal, JobJournal>();
builder.Services.AddSingleton<JobRunner>();

builder.Services.AddScoped<IOutbox, OutboxWriter>();
builder.Services.AddScoped<IIntegrationEventDispatcher, IntegrationEventDispatcher>();
builder.Services.AddHostedService<OutboxRelay>();

// Delivered rows do not accumulate for ever. Dead-lettered ones are never
// swept: those are events that will never arrive, and a timer must not erase
// the evidence of the failure this mechanism exists to make visible.
builder.Services.AddHostedService<OutboxRetentionSweep>();

// Accepts either the Platform's own setting or the DATABASE_URL that managed
// platforms publish, which Npgsql cannot parse on its own.
string resolvedConnectionString = ConnectionStringResolver.Resolve(builder.Configuration)
    ?? throw new InvalidOperationException(
        "No database connection is configured. Set CCP_ConnectionStrings__Platform, or "
        + "DATABASE_URL if your platform publishes one. See .env.example.");

// Every context, factory and raw connection inherits the Platform's limits from
// the string itself, because a rule that has to be repeated at twenty-two
// UseNpgsql call sites is a rule that is missing from at least one of them.
// Anything the deployment already specified is left alone.
string connectionString = ConnectionHardening.Apply(resolvedConnectionString);

// Migrations may run as a different role, and on a serious deployment they
// should. Changing the schema and serving requests are different privileges:
// one role holding both means a SQL-injection defect anywhere in the Platform is
// a defect that can drop a table, and the audit trail's append-only guarantee —
// a REVOKE, not a promise — can be granted straight back by the very connection
// it is meant to bind. Optional, so an environment that supplies one string goes
// on working exactly as it did.
//
// The migrator's connection also keeps the unhardened timeouts. pg_advisory_lock
// blocks until it is granted, and a statement timeout applies to a blocking
// statement — so a second instance waiting behind a long migration would have
// its wait cancelled and would go on to serve requests against a half-migrated
// schema.
string migrationConnectionString = ConnectionHardening.Apply(
    ConnectionStringResolver.ResolveMigrations(builder.Configuration) ?? resolvedConnectionString,
    serverSideTimeouts: false);

builder.Services
    .AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName));

builder.Services.AddSingleton<DatabaseMigrator>();

builder.Services.AddDbContext<KernelDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__ef_migrations_history", KernelDbContext.SchemaName)));

// ---------------------------------------------------------------------------
// API surface
// ---------------------------------------------------------------------------
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options =>
{
    // The permission each endpoint demands, written into the published contract
    // from the endpoint metadata itself. Phase 11 asks that an outside developer
    // integrate from the documentation alone, and a contract that lists shapes
    // but not permissions fails that on the first 403.
    options.AddOperationTransformer<SecurityAnnotationTransformer>();
    options.AddDocumentTransformer<PlatformDocumentTransformer>();
});

// Traces and metrics (ADR-015). Instrumented always; exported only where an
// OTLP endpoint has been configured, so a developer machine instruments and
// sends nothing.
builder.Services.AddPlatformObservability(
    builder.Configuration,
    serviceName: "company-central-platform",
    serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0");

builder.Services.AddHealthChecks()
    .AddDbContextCheck<KernelDbContext>(
        name: "database",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"])

    // Readiness that only asks about the database is readiness that lies. The
    // Platform serves most requests with object storage unreachable and no
    // request that touches a document, so a failing store should route traffic
    // away rather than accept uploads it will drop (§22.4).
    .AddCheck<DocumentStorageHealthCheck>(
        name: "document-storage",

        // Degraded, not unhealthy. Documents stop working; identity,
        // authorization and workflow do not, and taking the whole Platform out
        // of rotation because a bucket is unreachable would be the same mistake
        // that once stopped it starting over a folder.
        failureStatus: HealthStatus.Degraded,
        tags: ["ready"]);

// Rate limiting (ARCHITECTURE.md §12.6). Named policies per endpoint class,
// applied by the endpoints themselves — a single global limit generous enough
// for browsing is enormous for password guessing, and the two cannot share one
// number.
//
// The global limiter remains as a backstop for anything that declares no policy,
// so an endpoint added without one is still bounded.
// The budgets are configuration, not constants: the right number depends on how
// many people sit behind one public address and how chatty the frontend turns
// out to be, neither of which is knowable from here.
RateLimitOptions rateLimits = builder.Configuration
    .GetSection(RateLimitOptions.SectionName)
    .Get<RateLimitOptions>() ?? new RateLimitOptions();

builder.Services
    .AddOptions<RateLimitOptions>()
    .Bind(builder.Configuration.GetSection(RateLimitOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddRateLimiter(options =>
{
    options.AddPlatformPolicies(rateLimits);

    // Two chained limiters, both of which must admit a request.
    //
    // The first is the ordinary backstop for anything that declares no policy.
    // The second bounds authentication attempts per source address — the other
    // direction from the per-account limit the auth endpoints carry. Per-account
    // alone would let one machine try ten attempts against each of a thousand
    // names, which is precisely credential stuffing; per-address alone collapses
    // behind office NAT. Neither is sufficient, so both apply.
    options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.User.FindFirst("sub")?.Value
                    ?? context.Connection.RemoteIpAddress?.ToString()
                    ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 600,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                })),
        RateLimitPolicies.CreateAuthenticationSourceLimiter(rateLimits.AuthenticationPerAddress));
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

        // A claim should be called what the token calls it.
        //
        // By default the handler renames inbound claims to WS-Federation URIs —
        // `sub` arrives as
        // http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier —
        // so `FindFirst("sub")` finds nothing on a perfectly good token. That
        // silently broke every endpoint reading the subject without knowing to
        // look under the other name, and it is invisible at the call site: the
        // code looks right, compiles, and returns 401 to everyone.
        jwt.MapInboundClaims = false;

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

// Replaces the framework's bare 403 with a body that says which refusal it was:
// a permission the caller does not hold, or an elevation that has lapsed. The
// status is the same for both; the code is what the client acts on.
builder.Services.AddSingleton<
    IAuthorizationMiddlewareResultHandler,
    PlatformAuthorizationResultHandler>();

// ---------------------------------------------------------------------------
// Modules
// ---------------------------------------------------------------------------
// Module infrastructure is registered by the host, the only place permitted to
// know about an Api layer and an Infrastructure layer at once (§6.1).
builder.Services.AddIdentityInfrastructure(builder.Configuration, connectionString);
builder.Services.AddOrganizationInfrastructure(builder.Configuration, connectionString);
builder.Services.AddAuthorizationInfrastructure(builder.Configuration, connectionString);
builder.Services.AddSecurityInfrastructure(builder.Configuration, connectionString);
builder.Services.AddAuditInfrastructure(builder.Configuration, connectionString);
builder.Services.AddWorkflowInfrastructure(builder.Configuration, connectionString);
builder.Services.AddNotificationInfrastructure(builder.Configuration, connectionString);
builder.Services.AddDocumentInfrastructure(builder.Configuration, connectionString);
builder.Services.AddIntegrationInfrastructure(builder.Configuration, connectionString);
builder.Services.AddConfigurationInfrastructure(builder.Configuration, connectionString);

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

// Before the limiter, because the limiter partitions authentication attempts by
// the account being targeted and the account is in the request body.
app.UseMiddleware<AuthenticationTargetMiddleware>();

app.UseRateLimiter();
app.UseCors();
app.UseAuthentication();
// Between authentication and authorization, and the order is the point.
//
// The obligation is a property of the credential, not of the resource. Placed
// after authorization, the permission check refuses first -- so somebody holding
// a temporary password and no roles is told they lack a permission, which is
// true, useless, and hides the one thing they can actually do about it. Placed
// here, they are told to change their password, which is both the actionable
// answer and the more important one.
//
// Routing has already run, so the endpoint and its metadata are known; only the
// permission decision has not been made yet, and it does not need to be.
app.UseMiddleware<PasswordChangePendingMiddleware>();

app.UseAuthorization();

// After routing has chosen an endpoint, because the deprecation lives on the
// endpoint's metadata, and before anything writes a body. Nothing is deprecated
// today; the machinery exists so that the first one is a two-line change rather
// than a scramble to tell everybody (docs/api/versioning.md).
app.UseMiddleware<DeprecationHeaderMiddleware>();

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
// ---------------------------------------------------------------------------
// Schema
// ---------------------------------------------------------------------------
// Before anything reads or writes. Off unless asked for: on a serious
// deployment a schema change is a reviewed step, not a side effect of a
// restart. Enabled where the platform offers nowhere else to run it.
//
// Unlike the seeding below, a failure here is fatal. An application whose schema
// did not apply cannot serve a single request correctly, and starting anyway
// would turn one clear error into a stream of confusing ones.
DatabaseOptions databaseOptions =
    app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;

if (databaseOptions.ApplyMigrationsOnStartup)
{
    using IServiceScope migrationScope = app.Services.CreateScope();

    await app.Services.GetRequiredService<DatabaseMigrator>().MigrateAsync(
        migrationConnectionString,
        [
            migrationScope.ServiceProvider.GetRequiredService<KernelDbContext>(),
            migrationScope.ServiceProvider.GetRequiredService<IdentityDbContext>(),
            migrationScope.ServiceProvider.GetRequiredService<OrganizationDbContext>(),
            migrationScope.ServiceProvider.GetRequiredService<AuthorizationDbContext>(),
            migrationScope.ServiceProvider.GetRequiredService<SecurityDbContext>(),
            migrationScope.ServiceProvider.GetRequiredService<AuditDbContext>(),
            migrationScope.ServiceProvider.GetRequiredService<WorkflowDbContext>(),
            migrationScope.ServiceProvider.GetRequiredService<NotificationDbContext>(),
            migrationScope.ServiceProvider.GetRequiredService<DocumentDbContext>(),
            migrationScope.ServiceProvider.GetRequiredService<IntegrationDbContext>(),
            migrationScope.ServiceProvider.GetRequiredService<ConfigurationDbContext>()
        ],
        databaseOptions.ApplicationRole);
}

// ---------------------------------------------------------------------------
// What the password parameters cost here
// ---------------------------------------------------------------------------
// One hash, once, so the log says how long a sign-in's most expensive step takes
// on this machine. The parameters are a guess until something running says what
// they cost, and there was nowhere for that measurement to happen for twenty
// phases.
//
// It reports and does not tune: password hashing cost is a security parameter,
// and a Platform that raised its own would change every sign-in on a schedule
// nobody chose.
app.Services.GetRequiredService<PasswordCostReport>().Report();

// ---------------------------------------------------------------------------
// First administrator
// ---------------------------------------------------------------------------
// Off unless explicitly enabled, and it refuses to run once any user exists, so
// it can only ever create the *first* account and never becomes a back door
// into a running system.
//
// Before authorization seeding, because the account it creates needs the
// permissions that seeding declares.
//
// Failure is logged, not fatal: a Platform that cannot bootstrap should still
// come up and report itself, rather than crash-loop while hiding the reason.
BootstrapResult bootstrap = new(BootstrapOutcome.Disabled);

try
{
    bootstrap = await app.Services
        .GetRequiredService<BootstrapAdministratorSeeder>()
        .RunAsync();

    if (app.Logger.IsEnabled(LogLevel.Information))
    {
        app.Logger.LogInformation("Bootstrap: {Outcome}.", bootstrap.Outcome);
    }
}
catch (Exception exception)
{
    app.Logger.LogError(exception, "Bootstrapping the first administrator failed.");
}

// The templates the Platform sends for its own events. Before authorization
// seeding is unnecessary, but after the database is migrated is: a missing
// template makes the Platform's first notification fail silently.
try
{
    await app.Services.GetRequiredService<TemplateSeeder>().SeedAsync();
}
catch (Exception exception)
{
    app.Logger.LogError(exception, "Seeding notification templates failed.");
}

try
{
    var seeder = app.Services.GetRequiredService<AuthorizationSeeder>();

    // `app`, not `app.Services`. The EndpointDataSource in the container is a
    // composite that never sees the endpoints a minimal API maps; it answers
    // "none" without complaint.
    await seeder.SeedAsync(app);

    // The join between the two seeders, and the only place it can be made:
    // Identity created the account, Authorization owns the role, and neither
    // module may reach into the other (§6.2). This runs after seeding because
    // the role it grants is created there.
    //
    // Until this existed the first administrator was created holding nothing —
    // they could sign in, and every screen refused them. A Platform whose first
    // user can do nothing is not bootstrapped, whatever the log says.
    if (bootstrap is { Outcome: BootstrapOutcome.Created, UserId: { } newAdministrator })
    {
        await seeder.GrantAdministratorAsync(newAdministrator);
    }
    else if (bootstrap is { Outcome: BootstrapOutcome.AlreadyBootstrapped, UserId: { } existing })
    {
        // The repair path, for a Platform bootstrapped before the grant
        // existed: the account is there and holds nothing, so nobody can
        // administer anything and nobody can fix it either — granting requires
        // holding. It fires only while nothing at all is granted, so it closes
        // the moment the Platform becomes administrable.
        await seeder.GrantAdministratorAsync(existing, onlyIfNothingGranted: true);
    }
}
catch (Exception exception)
{
    app.Logger.LogError(
        exception,
        "Authorization seeding failed. Permissions may be missing until this is resolved.");
}

// ---------------------------------------------------------------------------
// The Platform's own settings
// ---------------------------------------------------------------------------
// A setting must be declared before it can be set, which is what gives it a
// type and a place on the Configuration screen. Without this an administrator
// wanting to change the document deletion grace period would have to declare it
// through the API first, guessing the key the Platform reads -- a worse
// experience than the deployment it replaces.
//
// The defaults are read from the options the Platform actually runs on, so the
// number on the screen and the number in the code are the same by construction
// rather than by two people remembering.
//
// Failure is logged, not fatal: every caller falls back to its compiled-in
// value, which is exactly how the Platform behaved before settings were
// changeable.
try
{
    using IServiceScope settingScope = app.Services.CreateScope();

    OutboxOptions outboxDefaults =
        settingScope.ServiceProvider.GetRequiredService<IOptions<OutboxOptions>>().Value;

    JobJournalOptions journalDefaults =
        settingScope.ServiceProvider.GetRequiredService<IOptions<JobJournalOptions>>().Value;

    await settingScope.ServiceProvider
        .GetRequiredService<PlatformSettingSeeder>()
        .SeedAsync(new PlatformSettingDefaults(
            OutboxProcessedRetention: outboxDefaults.ProcessedRetention,
            JobHistoryRetention: journalDefaults.Retention,
            IntegrationCallLogRetention: settingScope.ServiceProvider
                .GetRequiredService<IntegrationOptions>().CallLogRetention,
            DocumentDeletionGrace: settingScope.ServiceProvider
                .GetRequiredService<DocumentOptions>().DeletionGracePeriod));
}
catch (Exception exception)
{
    app.Logger.LogError(
        exception,
        "The Platform settings could not be declared. Every value falls back to "
        + "the one it shipped with, and none of them can be changed without a "
        + "deployment until this is resolved.");
}

await app.RunAsync();

/// <summary>
/// Exposed so the integration tests can construct the host through
/// <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program;
