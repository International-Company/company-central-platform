using CCP.Modules.Identity.Contracts;
using CCP.Modules.Notifications.Application.Abstractions;
using CCP.Modules.Notifications.Domain.Notifications;
using Microsoft.Extensions.Options;

namespace CCP.Modules.Notifications.Infrastructure;

/// <summary>Which language to write in when the person has not chosen one.</summary>
public sealed class RecipientOptions
{
    public const string SectionName = "Notifications:Recipients";

    /// <summary>
    /// The company's language.
    /// <para>
    /// Arabic, because that is what this Platform is for. A fallback of English
    /// would mean a deployment in an Arabic company sending English to everybody
    /// who never opened a settings screen — which is most people.
    /// </para>
    /// </summary>
    public string DefaultLocale { get; set; } = "ar";
}

/// <summary>
/// Finds where to write to somebody, using Identity's public surface.
/// <para>
/// <b>The file that keeps Notifications from knowing what an email address
/// is.</b> The contract lives in the Application layer; this is the only place
/// that knows the answer comes from Identity, and it reaches it through
/// <see cref="IUserDirectory"/> rather than through a schema (§6.2).
/// </para>
/// <para>
/// Language is the person's own choice where they have made one, and the
/// company's where they have not. For seven phases this returned the company
/// default to everybody, so an English speaker in an Arabic company received
/// Arabic — in a Platform whose two languages are meant to be equal.
/// </para>
/// </summary>
public sealed class PlatformRecipientDirectory(
    IUserDirectory users,
    IOptions<RecipientOptions> options) : IRecipientDirectory
{
    private readonly RecipientOptions _options = options.Value;

    public async Task<string?> GetAddressAsync(
        Guid userId,
        NotificationChannel channel,
        CancellationToken cancellationToken = default)
        => channel switch
        {
            NotificationChannel.Email => await users.GetEmailAsync(userId, cancellationToken),

            // The in-app channel needs no address: the notification row is the
            // delivery. SMS and Push are designed for and not built, and
            // returning null means the dispatcher will not queue for them.
            _ => null
        };

    public async Task<string> GetLocaleAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        string? chosen = await users.GetPreferredLocaleAsync(userId, cancellationToken);

        // The company default when they have not chosen. Not stored on the
        // account at sign-up, so somebody who never chose follows the company if
        // it later changes its language -- which is what "I have no preference"
        // ought to mean.
        return string.IsNullOrWhiteSpace(chosen) ? _options.DefaultLocale : chosen;
    }
}
