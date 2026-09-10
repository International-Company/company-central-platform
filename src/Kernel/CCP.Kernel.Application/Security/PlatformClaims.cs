namespace CCP.Kernel.Application.Security;

/// <summary>
/// Claim names the Platform mints and reads.
/// <para>
/// <b>In the application layer because both ends need them.</b> Identity's token
/// service writes these and the API pipeline reads them, and those live in
/// projects that cannot see each other — Identity's infrastructure does not
/// reference the API layer, by design. A constant in either one would be a
/// string literal in the other.
/// </para>
/// <para>
/// That is not a hypothetical: the same shape produced two metrics declared in a
/// project no caller could reach, both with alerts written against them, both
/// reading permanently healthy. A name shared across a boundary belongs where
/// the boundary can see it.
/// </para>
/// </summary>
public static class PlatformClaims
{
    /// <summary>
    /// Present, and <c>"true"</c>, when the holder owes a password change.
    /// <para>
    /// Absent means no obligation — which is what every token minted before this
    /// existed says, so shipping the check does not refuse everybody holding an
    /// older token.
    /// </para>
    /// </summary>
    public const string MustChangePassword = "must_change_password";
}
