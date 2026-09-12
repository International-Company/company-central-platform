namespace CCP.Modules.Authorization.Domain.Roles;

/// <summary>
/// What "administrator" means, in exactly one place.
/// <para>
/// The permission that hands out access, and therefore the only permission whose
/// complete disappearance cannot be undone: without somebody holding it, nobody
/// can be given anything ever again, through any endpoint the Platform offers.
/// </para>
/// <para>
/// <b>There is deliberately no administrator flag on a user.</b> Inventing one
/// for this would put a second definition of privilege beside the one the
/// Platform actually enforces, and the two would disagree on the day it
/// mattered.
/// </para>
/// </summary>
public static class AdministratorSafety
{
    /// <summary>Granting a role to somebody: the way back from anything.</summary>
    public const string GrantPermission = "platform.roles.assign";
}
