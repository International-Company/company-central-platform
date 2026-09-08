using System.ComponentModel.DataAnnotations;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Identity.Application;
using CCP.Modules.Identity.Application.Abstractions;
using CCP.Modules.Identity.Domain.Credentials;
using CCP.Modules.Identity.Domain.Users;
using CCP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Identity.Infrastructure.Bootstrap;

/// <summary>Settings for the one-time bootstrap administrator.</summary>
public sealed class BootstrapOptions
{
    public const string SectionName = "Identity:Bootstrap";

    /// <summary>
    /// Whether to attempt bootstrapping at startup. Off by default: creating an
    /// administrator is a deliberate, supervised act, not something a
    /// deployment does because a configuration file happened to be present.
    /// </summary>
    public bool Enabled { get; set; }

    [MaxLength(64)]
    public string? Username { get; set; }

    [MaxLength(256)]
    public string? Email { get; set; }

    [MaxLength(128)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// The initial password.
    /// <para>
    /// <b>This is a secret and must come from the secret manager or an
    /// environment variable, never from a committed file</b>
    /// (ARCHITECTURE.md §12.7). The account is created with
    /// <c>MustChangePassword</c>, so it is a one-time handover credential: the
    /// person who performs the bootstrap cannot keep using it, and it stops
    /// working the moment the real administrator signs in.
    /// </para>
    /// </summary>
    public string? InitialPassword { get; set; }
}

/// <summary>
/// Creates the first administrator, once, on an empty Platform.
/// <para>
/// Every system needs one account that exists before any account can be
/// administered, and that account is a standing risk: it is the one credential
/// nobody granted. The protections here are deliberate:
/// </para>
/// <list type="number">
/// <item><b>Refuses to run if any user exists.</b> This can only ever create the
/// first account, so it can never be used to quietly add a second
/// administrator to a live system.</item>
/// <item><b>Off by default.</b> Enabling it is an explicit act.</item>
/// <item><b>No default credentials.</b> If the username, email or password is
/// missing, it fails loudly rather than inventing something. A hardcoded
/// default administrator password is how systems get breached on day one.</item>
/// <item><b>Must change password at first sign-in.</b> The handover credential
/// is not a lasting one.</item>
/// <item><b>Logged prominently and audited.</b> The creation raises a normal
/// <c>UserCreatedEvent</c>, so it appears in the audit trail like any other
/// account — but with a warning-level log making clear which account it was.</item>
/// </list>
/// <para>
/// <b>Open question Q10</b> — who the bootstrap administrator is, and the
/// procedure for running this in production — is still unanswered. The
/// mechanism is built and safe; the operational procedure needs the project
/// owner. See <c>docs/identity/bootstrap-administrator.md</c>.
/// </para>
/// </summary>
public sealed class BootstrapAdministratorSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<BootstrapOptions> options,
    IClock clock,
    ILogger<BootstrapAdministratorSeeder> logger)
{
    private readonly BootstrapOptions _options = options.Value;

    /// <summary>
    /// Runs the bootstrap if it is enabled and the Platform has no users.
    /// Returns what happened, so a caller can report it rather than guess.
    /// </summary>
    public async Task<BootstrapOutcome> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return BootstrapOutcome.Disabled;
        }

        using IServiceScope scope = scopeFactory.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IIdentityRepository>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>();
        var identityOptions = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;

        // The decisive guard. This can only create the *first* account, so it is
        // not a back door into a running system.
        if (await dbContext.Users.AnyAsync(cancellationToken))
        {
            logger.LogInformation(
                "Bootstrap is enabled but the Platform already has users. Nothing was created. "
                + "Disable Identity:Bootstrap:Enabled.");

            return BootstrapOutcome.AlreadyBootstrapped;
        }

        if (string.IsNullOrWhiteSpace(_options.Username)
            || string.IsNullOrWhiteSpace(_options.Email)
            || string.IsNullOrWhiteSpace(_options.InitialPassword))
        {
            // Fails loudly rather than inventing a default. A well-known
            // administrator password is how a system is compromised on day one.
            throw new InvalidOperationException(
                "Identity:Bootstrap is enabled but incomplete. Username, Email and InitialPassword "
                + "are all required, and InitialPassword must come from the secret manager or an "
                + "environment variable. The Platform will not invent a default administrator.");
        }

        DateTimeOffset now = clock.UtcNow;

        Result<User> creation = User.Create(
            _options.Username,
            _options.Email,
            string.IsNullOrWhiteSpace(_options.DisplayName) ? _options.Username : _options.DisplayName,
            now,
            mustChangePassword: true);

        if (creation.IsFailure)
        {
            throw new InvalidOperationException(
                "The bootstrap administrator details are invalid: "
                + string.Join("; ", creation.Errors.Select(e => e.Message)));
        }

        User administrator = creation.Value;

        // The policy applies here too. A bootstrap account is not exempt from
        // the password rules — it is the account that most needs them.
        Result policyResult = identityOptions.Password.Validate(
            _options.InitialPassword, administrator.Username);

        if (policyResult.IsFailure)
        {
            throw new InvalidOperationException(
                "The bootstrap administrator password does not satisfy the password policy: "
                + string.Join("; ", policyResult.Errors.Select(e => e.Message)));
        }

        repository.AddUser(administrator);

        repository.AddCredential(UserCredential.Create(
            administrator.Id,
            passwordHasher.Hash(_options.InitialPassword),
            passwordHasher.AlgorithmId,
            now));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Warning level deliberately: this is a rare, security-significant event
        // and should stand out in the log, not blend into startup noise.
        logger.LogWarning(
            "Bootstrap administrator created. Username={Username} UserId={UserId}. "
            + "The account must change its password at first sign-in. "
            + "Disable Identity:Bootstrap:Enabled now, and remove the initial password from "
            + "configuration and from the secret manager.",
            administrator.Username,
            administrator.Id);

        return BootstrapOutcome.Created;
    }
}

/// <summary>What a bootstrap attempt did.</summary>
public enum BootstrapOutcome
{
    /// <summary>Bootstrapping is switched off. The normal state.</summary>
    Disabled = 1,

    /// <summary>Users already exist, so nothing was created.</summary>
    AlreadyBootstrapped = 2,

    /// <summary>The first administrator was created.</summary>
    Created = 3
}
