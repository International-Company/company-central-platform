using CCP.Modules.Identity.Application;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Refuses to start on a passkey configuration no browser would honour.
/// <para>
/// <b>It names every problem rather than reporting that there is one.</b> The
/// symptom of a mismatch between the relying party and the origin is the
/// browser declining, silently as far as the server is concerned: nothing
/// reaches the Platform, nothing is logged, and the person is told only that
/// their device could not be used. A failure at startup that says which setting
/// is wrong and why turns a day of that into a line in a log.
/// </para>
/// </summary>
internal sealed class WebAuthnOptionsValidator : IValidateOptions<IdentityOptions>
{
    public ValidateOptionsResult Validate(string? name, IdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<string> problems = options.WebAuthn.Misconfigurations();

        return problems.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(problems);
    }
}
