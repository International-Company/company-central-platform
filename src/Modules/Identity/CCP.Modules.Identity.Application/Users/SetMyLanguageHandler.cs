using CCP.Kernel.Application.Auditing;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Identity.Application.Abstractions;
using CCP.Modules.Identity.Contracts.Dtos;
using CCP.Modules.Identity.Domain.Users;

namespace CCP.Modules.Identity.Application.Users;

/// <summary>Choosing the language the Platform writes to you in.</summary>
/// <param name="Locale">
/// <c>ar</c> or <c>en</c>, or null to return to the company default.
/// </param>
public sealed record SetMyLanguageCommand(Guid UserId, string? Locale);

/// <summary>
/// Records a person's own choice of language.
/// <para>
/// <b>It is about email, not about the portal.</b> The portal already knows
/// which language it is showing, from the URL it was opened at. An email arrives
/// with nobody present to have opened anything, so the only way it can be in the
/// right language is for the choice to have been written down. Until it was,
/// every notification went out in the company default, which meant an English
/// speaker in an Arabic company received Arabic — in a Platform whose two
/// languages are supposed to be equal.
/// </para>
/// <para>
/// <b>Their own account only.</b> The command carries the id from the token
/// rather than from the request body, so there is no shape of this call that
/// changes somebody else's language. An administrator wanting to set a
/// colleague's preference would be a different endpoint with a permission on it,
/// and nobody has asked for one.
/// </para>
/// </summary>
public sealed class SetMyLanguageHandler(
    IIdentityRepository repository,
    IIdentityUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IClock clock)
{
    public async Task<Result<UserDto>> HandleAsync(
        SetMyLanguageCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        User? user = await repository.FindUserByIdAsync(command.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<UserDto>(IdentityErrors.UserNotFound);
        }

        Result chosen = user.ChooseLocale(command.Locale, clock.UtcNow);

        if (chosen.IsFailure)
        {
            return Result.Failure<UserDto>(chosen.Errors);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Audited, mildly against expectation. It is a preference rather than a
        // privilege, and it is still the answer to "why did this month's
        // notices arrive in English" -- which is a question somebody does ask,
        // and one that has no other answer anywhere.
        await auditTrail.RecordAsync(
            new AuditEntry(
                "identity",
                "user.language-chosen",
                AuditOutcome.Success,
                "user",
                user.Id.ToString(),
                NewValue: $$"""{"preferredLocale":{{Quoted(user.PreferredLocale)}}}"""),
            cancellationToken);

        return Result.Success(UserMapper.ToDto(user));
    }

    /// <summary>
    /// A JSON string, or a bare <c>null</c> for the cleared case.
    /// <para>
    /// <c>"null"</c> in quotes and a real null read identically in a trail
    /// somebody is skimming, and they mean different things: one is a language
    /// and the other is the absence of a choice.
    /// </para>
    /// </summary>
    private static string Quoted(string? value) => value is null ? "null" : $"\"{value}\"";
}
